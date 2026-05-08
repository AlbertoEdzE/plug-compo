from __future__ import annotations

import json
import time
from dataclasses import asdict
from typing import Any, Callable, Optional

from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from openai import AsyncAzureOpenAI

from contracts import AiEmailTriageAdapter, EmailTriageRequest, EmailTriageResult, ExtractedEmailEntity
from options import AiEmailTriageOptions
from prompts import TRIAGE_SYSTEM_PROMPT, TRIAGE_USER_TEMPLATE


class AzureOpenAiEmailTriageAdapter(AiEmailTriageAdapter):
    def __init__(
        self,
        options: AiEmailTriageOptions,
        azure_ad_token_provider: Optional[Callable[[], str]] = None,
    ) -> None:
        if not options.azure_openai_endpoint:
            raise ValueError("azure_openai_endpoint is required for AzureOpenAi provider")

        token_provider = azure_ad_token_provider or get_bearer_token_provider(
            DefaultAzureCredential(),
            "https://cognitiveservices.azure.com/.default",
        )

        self._client = AsyncAzureOpenAI(
            azure_endpoint=options.azure_openai_endpoint,
            azure_ad_token_provider=token_provider,
            api_version=options.api_version,
        )
        self._options = options

    async def triage_async(self, request: EmailTriageRequest) -> EmailTriageResult:
        start = time.monotonic()

        if not request.body_text or not request.body_text.strip():
            return _safe_default_result(
                request=request,
                prompt_version=self._options.prompt_version,
                model_version="azure-openai",
                latency_ms=int((time.monotonic() - start) * 1000),
            )

        body = request.body_text
        if len(body) > self._options.max_body_chars:
            body = body[: self._options.max_body_chars] + "\n[truncated]"

        messages = [
            {"role": "system", "content": TRIAGE_SYSTEM_PROMPT},
            {
                "role": "user",
                "content": TRIAGE_USER_TEMPLATE.format(
                    subject=request.subject,
                    sender_name=request.sender_name or "",
                    sender_email=request.sender_email,
                    attachment_names=", ".join(request.attachment_names) or "none",
                    body_text=body,
                ),
            },
        ]

        data: Optional[dict[str, Any]] = None
        model_version = "azure-openai"

        for attempt in range(2):
            try:
                response = await self._client.chat.completions.create(
                    model=self._options.deployment_name,
                    messages=messages,
                    temperature=self._options.temperature,
                    max_tokens=800,
                    response_format={"type": "json_object"},
                )
                model_version = getattr(response, "model", model_version)
                content = response.choices[0].message.content or ""
                data = json.loads(content)
                break
            except json.JSONDecodeError:
                if attempt == 0:
                    continue
                data = None
            except Exception:
                data = None
                break

        latency_ms = int((time.monotonic() - start) * 1000)
        if data is None:
            return _safe_default_result(
                request=request,
                prompt_version=self._options.prompt_version,
                model_version=model_version,
                latency_ms=latency_ms,
            )

        entities = []
        for raw in data.get("entities", []) or []:
            try:
                entities.append(ExtractedEmailEntity(**raw))
            except TypeError:
                continue

        return EmailTriageResult(
            email_id=request.email_id,
            intent=str(data.get("intent", "Other")),
            intent_confidence=float(data.get("intent_confidence", 0.0)),
            extracted_entities=entities,
            routing_suggestion=str(data.get("routing_suggestion", "Manual")),
            urgency=str(data.get("urgency", "Normal")),
            urgency_signals=list(data.get("urgency_signals", []) or []),
            summary=str(data.get("summary", "")),
            model_version=model_version,
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id,
        )


def _safe_default_result(
    request: EmailTriageRequest,
    prompt_version: str,
    model_version: str,
    latency_ms: int,
) -> EmailTriageResult:
    return EmailTriageResult(
        email_id=request.email_id,
        intent="Other",
        intent_confidence=0.0,
        extracted_entities=[],
        routing_suggestion="Manual",
        urgency="Normal",
        urgency_signals=[],
        summary=f"Email from {request.sender_email} — Other.",
        model_version=model_version,
        prompt_version=prompt_version,
        latency_ms=latency_ms,
        correlation_id=request.correlation_id,
    )

