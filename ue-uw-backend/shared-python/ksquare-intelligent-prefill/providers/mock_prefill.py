import time

from contracts import IntelligentPrefillAdapter, PrefillFieldResult, PrefillRequest, PrefillResult
from options import IntelligentPrefillOptions


class MockPrefillAdapter(IntelligentPrefillAdapter):
    def __init__(self, options: IntelligentPrefillOptions) -> None:
        self._options = options

    async def prefill_async(self, request: PrefillRequest) -> PrefillResult:
        start = time.monotonic()

        results = [
            PrefillFieldResult(
                canonical_field=f.canonical_field,
                value="MOCK_VALUE",
                confidence=0.80,
                source_text="",
                reasoning="Mock prefill adapter output.",
                needs_review=False,
            )
            for f in request.unmapped_fields
        ]

        latency_ms = int((time.monotonic() - start) * 1000)
        threshold = self._options.review_confidence_threshold
        for r in results:
            r.needs_review = r.confidence < threshold

        filled = sum(1 for r in results if r.confidence >= 0.50)
        needs_review = sum(1 for r in results if r.needs_review)

        return PrefillResult(
            document_id=request.document_id,
            field_results=results,
            total_fields_requested=len(request.unmapped_fields),
            total_fields_filled=filled,
            total_needs_review=needs_review,
            model_version="mock",
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id,
        )
