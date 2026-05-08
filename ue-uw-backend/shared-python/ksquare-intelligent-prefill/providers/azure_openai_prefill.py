from __future__ import annotations

import json
import time
from typing import Any, Callable, Optional

from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from openai import AsyncAzureOpenAI

from contracts import (
    IntelligentPrefillAdapter,
    PrefillFieldResult,
    PrefillRequest,
    PrefillResult,
    UnmappedField,
)
from options import IntelligentPrefillOptions
from prompts import PREFILL_SYSTEM_PROMPT, PREFILL_USER_TEMPLATE


class AzureOpenAiPrefillAdapter(IntelligentPrefillAdapter):
    def __init__(
        self,
        options: IntelligentPrefillOptions,
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
            api_version="2025-01-01-preview",
        )
        self._options = options

    async def prefill_async(self, request: PrefillRequest) -> PrefillResult:
        start = time.monotonic()

        if not request.unmapped_fields:
            return PrefillResult(
                document_id=request.document_id,
                field_results=[],
                total_fields_requested=0,
                total_fields_filled=0,
                total_needs_review=0,
                model_version=self._options.deployment_name,
                prompt_version=self._options.prompt_version,
                latency_ms=int((time.monotonic() - start) * 1000),
                correlation_id=request.correlation_id,
            )

        if not request.document_text or not request.document_text.strip():
            return PrefillResult(
                document_id=request.document_id,
                field_results=[],
                total_fields_requested=len(request.unmapped_fields),
                total_fields_filled=0,
                total_needs_review=0,
                model_version=self._options.deployment_name,
                prompt_version=self._options.prompt_version,
                latency_ms=int((time.monotonic() - start) * 1000),
                correlation_id=request.correlation_id,
            )

        doc_text = request.document_text[: self._options.max_document_chars]

        fields_per_batch = max(int(self._options.fields_per_batch), 1)
        batches = [
            request.unmapped_fields[i : i + fields_per_batch]
            for i in range(0, len(request.unmapped_fields), fields_per_batch)
        ]

        all_results: list[PrefillFieldResult] = []
        for batch in batches:
            batch_results = await self._extract_batch(doc_text, request.document_type, batch)
            all_results.extend(batch_results)

        latency_ms = int((time.monotonic() - start) * 1000)
        threshold = self._options.review_confidence_threshold
        for r in all_results:
            r.needs_review = r.confidence < threshold

        filled = sum(1 for r in all_results if r.confidence >= 0.50)
        needs_review = sum(1 for r in all_results if r.needs_review)

        return PrefillResult(
            document_id=request.document_id,
            field_results=all_results,
            total_fields_requested=len(request.unmapped_fields),
            total_fields_filled=filled,
            total_needs_review=needs_review,
            model_version=self._options.deployment_name,
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id,
        )

    async def _extract_batch(
        self,
        doc_text: str,
        doc_type: str,
        fields: list[UnmappedField],
    ) -> list[PrefillFieldResult]:
        fields_json = json.dumps(
            [
                {
                    "canonical_field": f.canonical_field,
                    "display_label": f.display_label,
                    "expected_type": f.expected_type,
                    "description": f.description,
                }
                for f in fields
            ],
            indent=2,
        )

        messages = [
            {"role": "system", "content": PREFILL_SYSTEM_PROMPT},
            {
                "role": "user",
                "content": PREFILL_USER_TEMPLATE.format(
                    document_type=doc_type,
                    max_chars=self._options.max_document_chars,
                    document_text=doc_text,
                    fields_json=fields_json,
                ),
            },
        ]

        data: Optional[dict[str, Any]] = None
        for attempt in range(2):
            try:
                response = await self._client.chat.completions.create(
                    model=self._options.deployment_name,
                    messages=messages,
                    temperature=0.0,
                    max_tokens=1500,
                    response_format={"type": "json_object"},
                )
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

        if data is None:
            return _safe_batch_fallback(fields, self._options.review_confidence_threshold)

        raw_results = data.get("results", []) or []
        by_field: dict[str, PrefillFieldResult] = {}
        for raw in raw_results:
            if not isinstance(raw, dict):
                continue
            canonical = str(raw.get("canonical_field", "") or "")
            if not canonical:
                continue
            try:
                by_field[canonical] = PrefillFieldResult(
                    canonical_field=canonical,
                    value=None if raw.get("value", None) is None else str(raw.get("value", "")),
                    confidence=float(raw.get("confidence", 0.0)),
                    source_text=str(raw.get("source_text", "") or ""),
                    reasoning=str(raw.get("reasoning", "") or ""),
                    needs_review=False,
                )
            except Exception:
                continue

        results: list[PrefillFieldResult] = []
        for f in fields:
            results.append(
                by_field.get(
                    f.canonical_field,
                    PrefillFieldResult(
                        canonical_field=f.canonical_field,
                        value=None,
                        confidence=0.0,
                        source_text="",
                        reasoning="",
                        needs_review=True,
                    ),
                )
            )

        threshold = self._options.review_confidence_threshold
        for r in results:
            r.needs_review = r.confidence < threshold

        return results


def _safe_batch_fallback(
    fields: list[UnmappedField],
    review_confidence_threshold: float,
) -> list[PrefillFieldResult]:
    results = [
        PrefillFieldResult(
            canonical_field=f.canonical_field,
            value=None,
            confidence=0.0,
            source_text="",
            reasoning="",
            needs_review=True,
        )
        for f in fields
    ]
    for r in results:
        r.needs_review = r.confidence < review_confidence_threshold
    return results
