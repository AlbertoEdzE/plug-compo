from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any, Callable, Optional

from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from openai import AsyncAzureOpenAI

from contracts import (
    DocumentNarrativeAdapter,
    LossHistoryContext,
    NarrativeRequest,
    NarrativeResult,
    NarrativeType,
    SubmissionContext,
)
from options import DocumentNarrativeOptions
from prompts import (
    FILE_NOTE_SYSTEM,
    FILE_NOTE_USER,
    LOSS_RUN_SYSTEM,
    LOSS_RUN_USER,
    REFERRAL_MEMO_SYSTEM,
    REFERRAL_MEMO_USER,
    RISK_SUMMARY_SYSTEM,
    RISK_SUMMARY_USER,
)

try:
    from opentelemetry import trace

    _tracer = trace.get_tracer("ksquare.document_narrative", "1.0.0")
except Exception:
    trace = None
    _tracer = None


@dataclass(frozen=True)
class LlmTraceRecord:
    operation: str
    model: str
    prompt_tokens: int
    completion_tokens: int
    latency_ms: int
    correlation_id: Optional[str]


class LlmTracer:
    def __init__(self) -> None:
        self.records: list[LlmTraceRecord] = []

    def record(
        self,
        operation: str,
        model: str,
        prompt_tokens: int,
        completion_tokens: int,
        latency_ms: int,
        correlation_id: Optional[str],
    ) -> None:
        self.records.append(
            LlmTraceRecord(
                operation=operation,
                model=model,
                prompt_tokens=prompt_tokens,
                completion_tokens=completion_tokens,
                latency_ms=latency_ms,
                correlation_id=correlation_id,
            )
        )


class AzureOpenAiNarrativeAdapter(DocumentNarrativeAdapter):
    PROMPTS: dict[NarrativeType, tuple[str, str]] = {
        NarrativeType.RISK_SUMMARY: (RISK_SUMMARY_SYSTEM, RISK_SUMMARY_USER),
        NarrativeType.LOSS_RUN_NARRATIVE: (LOSS_RUN_SYSTEM, LOSS_RUN_USER),
        NarrativeType.REFERRAL_MEMO: (REFERRAL_MEMO_SYSTEM, REFERRAL_MEMO_USER),
        NarrativeType.UNDERWRITER_FILE_NOTE: (FILE_NOTE_SYSTEM, FILE_NOTE_USER),
    }

    def __init__(
        self,
        options: DocumentNarrativeOptions,
        azure_ad_token_provider: Optional[Callable[[], str]] = None,
        tracer: Optional[LlmTracer] = None,
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
        self._tracer = tracer or LlmTracer()

    async def generate_narrative_async(self, request: NarrativeRequest) -> NarrativeResult:
        start = time.monotonic()
        system_prompt, user_template = self.PROMPTS[request.narrative_type]
        user_message = self._build_user_message(user_template, request)

        operation_name = f"narrative_{request.narrative_type.value.lower()}"
        max_tokens = self._max_tokens_for(request.narrative_type)

        try:
            if _tracer is not None:
                with _tracer.start_as_current_span(name=f"gen_ai.chat", kind=trace.SpanKind.CLIENT) as span:
                    span.set_attribute("gen_ai.system", "az.ai.openai")
                    span.set_attribute("gen_ai.operation.name", "chat")
                    span.set_attribute("gen_ai.request.model", self._options.deployment_name)
                    span.set_attribute("ksquare.operation", operation_name)
                    response = await self._client.chat.completions.create(
                        model=self._options.deployment_name,
                        messages=[
                            {"role": "system", "content": system_prompt},
                            {"role": "user", "content": user_message},
                        ],
                        temperature=self._options.temperature,
                        max_tokens=max_tokens,
                    )
            else:
                response = await self._client.chat.completions.create(
                    model=self._options.deployment_name,
                    messages=[
                        {"role": "system", "content": system_prompt},
                        {"role": "user", "content": user_message},
                    ],
                    temperature=self._options.temperature,
                    max_tokens=max_tokens,
                )
        except Exception:
            latency_ms = int((time.monotonic() - start) * 1000)
            return NarrativeResult(
                submission_id=request.submission_id,
                narrative_type=request.narrative_type,
                narrative_text="",
                sections={},
                word_count=0,
                model_version="azure-openai",
                prompt_version=self._options.prompt_version,
                latency_ms=latency_ms,
                correlation_id=request.correlation_id,
            )

        latency_ms = int((time.monotonic() - start) * 1000)
        narrative_text = (response.choices[0].message.content or "").strip()
        sections = self._parse_sections(narrative_text, request.narrative_type)

        usage = getattr(response, "usage", None)
        prompt_tokens = int(getattr(usage, "prompt_tokens", 0) or 0)
        completion_tokens = int(getattr(usage, "completion_tokens", 0) or 0)

        self._tracer.record(
            operation=operation_name,
            model=self._options.deployment_name,
            prompt_tokens=prompt_tokens,
            completion_tokens=completion_tokens,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id,
        )

        return NarrativeResult(
            submission_id=request.submission_id,
            narrative_type=request.narrative_type,
            narrative_text=narrative_text,
            sections=sections,
            word_count=len(narrative_text.split()),
            model_version=str(getattr(response, "model", self._options.deployment_name)),
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id,
        )

    @property
    def tracer(self) -> LlmTracer:
        return self._tracer

    def _build_user_message(self, user_template: str, request: NarrativeRequest) -> str:
        sc: SubmissionContext = request.submission_context
        risk_indicators_formatted = _format_risk_indicators(sc.risk_indicators)
        coverage_lines_formatted = _format_coverage_lines(sc.coverage_lines)
        loss_history_formatted = _format_loss_history(request.loss_history)
        loss_run_table = _format_loss_run_table(request.loss_history)

        values: dict[str, Any] = {
            "submission_id": request.submission_id,
            "institution_name": sc.institution_name,
            "institution_type": sc.institution_type,
            "state": sc.state,
            "naics_code": sc.naics_code,
            "total_insured_value": sc.total_insured_value,
            "enrollment": sc.enrollment,
            "fte_employees": sc.fte_employees,
            "effective_date": sc.effective_date,
            "expiration_date": sc.expiration_date,
            "risk_indicators_formatted": risk_indicators_formatted,
            "coverage_lines_formatted": coverage_lines_formatted,
            "appetite_fit_score": sc.appetite_fit_score,
            "appetite_classification": sc.appetite_classification,
            "underwriter_name": request.underwriter_name or "",
            "additional_notes": request.additional_notes or "",
            "loss_history_formatted": loss_history_formatted,
            "loss_run_table": loss_run_table,
            "five_year_avg_loss_ratio": request.loss_history.five_year_avg_loss_ratio if request.loss_history else 0.0,
            "largest_single_loss": request.loss_history.largest_single_loss if request.loss_history else 0.0,
            "total_claims_count": request.loss_history.total_claims_count if request.loss_history else 0,
            "loss_trend": request.loss_history.loss_trend if request.loss_history else "Stable",
        }
        try:
            return user_template.format(**values)
        except Exception:
            return user_template

    def _max_tokens_for(self, narrative_type: NarrativeType) -> int:
        return {
            NarrativeType.RISK_SUMMARY: 200,
            NarrativeType.LOSS_RUN_NARRATIVE: 250,
            NarrativeType.REFERRAL_MEMO: 600,
            NarrativeType.UNDERWRITER_FILE_NOTE: 800,
        }[narrative_type]

    def _parse_sections(self, text: str, narrative_type: NarrativeType) -> dict[str, str]:
        if narrative_type in (NarrativeType.REFERRAL_MEMO, NarrativeType.UNDERWRITER_FILE_NOTE):
            import re

            sections: dict[str, str] = {}
            parts = re.split(r"\n\d+\.\s+([A-Z\s]+):\n", "\n" + text)
            for i in range(1, len(parts) - 1, 2):
                sections[parts[i].strip()] = parts[i + 1].strip()
            return sections if sections else {"full": text}
        return {"full": text}


def _format_risk_indicators(risk_indicators: dict) -> str:
    if not risk_indicators:
        return "none"
    lines = []
    for k in sorted(risk_indicators.keys(), key=lambda x: str(x)):
        v = risk_indicators.get(k)
        lines.append(f"- {k}: {v}")
    return "\n".join(lines)


def _format_coverage_lines(coverage_lines: list[dict]) -> str:
    if not coverage_lines:
        return "none"
    lines = []
    for line in coverage_lines:
        product = line.get("product", "")
        limit_v = line.get("limit", None)
        premium_v = line.get("premium", None)
        limit_s = f"{limit_v}" if limit_v is not None else ""
        premium_s = f"{premium_v}" if premium_v is not None else ""
        lines.append(f"- {product} | limit={limit_s} | premium={premium_s}")
    return "\n".join(lines)


def _format_loss_run_table(loss_history: LossHistoryContext | None) -> str:
    if loss_history is None or not loss_history.loss_run_years:
        return "not provided"
    rows = []
    for row in loss_history.loss_run_years:
        rows.append(f"- {row.get('year')}: incurred={row.get('incurred')}, claims={row.get('claims')}")
    return "\n".join(rows)


def _format_loss_history(loss_history: LossHistoryContext | None) -> str:
    if loss_history is None:
        return "not provided"
    return (
        f"5-Year Average Loss Ratio: {loss_history.five_year_avg_loss_ratio:.1%}\n"
        f"Largest Single Loss: ${loss_history.largest_single_loss:,.0f}\n"
        f"Total Claims (5 years): {loss_history.total_claims_count}\n"
        f"Trend: {loss_history.loss_trend}"
    )
