from __future__ import annotations

import time

from contracts import DocumentNarrativeAdapter, NarrativeRequest, NarrativeResult, NarrativeType
from options import DocumentNarrativeOptions


class MockNarrativeAdapter(DocumentNarrativeAdapter):
    def __init__(self, options: DocumentNarrativeOptions) -> None:
        self._options = options

    async def generate_narrative_async(self, request: NarrativeRequest) -> NarrativeResult:
        start = time.monotonic()
        sc = request.submission_context

        if request.narrative_type == NarrativeType.RISK_SUMMARY:
            text = f"Mock risk summary for {sc.institution_name}. Appetite fit: {sc.appetite_classification}."
        elif request.narrative_type == NarrativeType.LOSS_RUN_NARRATIVE:
            ratio = request.loss_history.five_year_avg_loss_ratio if request.loss_history else 0.0
            text = f"Mock loss run narrative. 5-year average loss ratio: {ratio:.1%}."
        elif request.narrative_type == NarrativeType.REFERRAL_MEMO:
            text = (
                "1. SUBMISSION OVERVIEW:\n"
                "Mock content.\n"
                "2. KEY RISK FACTORS:\n"
                "- Mock content.\n"
                "- Mock content.\n"
                "3. LOSS HISTORY SUMMARY:\n"
                "Mock content.\n"
                "4. APPETITE ASSESSMENT:\n"
                "Mock content.\n"
                "5. REFERRAL REASON:\n"
                "Mock content.\n"
                "6. RECOMMENDED ACTION:\n"
                "Request Additional Information\n"
            )
        else:
            text = (
                "1. SUBMISSION:\n"
                "Mock content.\n"
                "2. COVERAGE STRUCTURE:\n"
                "Mock content.\n"
                "3. RISK ASSESSMENT:\n"
                "Mock content.\n"
                "4. LOSS EXPERIENCE:\n"
                "Mock content.\n"
                "5. SPECIAL CONDITIONS:\n"
                "Mock content.\n"
                "6. UNDERWRITER NOTES:\n"
                "Mock content.\n"
            )

        latency_ms = int((time.monotonic() - start) * 1000)
        sections = _parse_sections(text, request.narrative_type)

        return NarrativeResult(
            submission_id=request.submission_id,
            narrative_type=request.narrative_type,
            narrative_text=text,
            sections=sections,
            word_count=len(text.split()),
            model_version="mock",
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id,
        )


def _parse_sections(text: str, narrative_type: NarrativeType) -> dict[str, str]:
    if narrative_type in (NarrativeType.REFERRAL_MEMO, NarrativeType.UNDERWRITER_FILE_NOTE):
        import re

        sections: dict[str, str] = {}
        parts = re.split(r"\n\d+\.\s+([A-Z\s]+):\n", "\n" + text)
        for i in range(1, len(parts) - 1, 2):
            sections[parts[i].strip()] = parts[i + 1].strip()
        return sections if sections else {"full": text}
    return {"full": text}
