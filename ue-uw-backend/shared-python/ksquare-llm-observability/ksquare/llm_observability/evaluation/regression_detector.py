from __future__ import annotations

from ..models import EvaluationRunResult, MetricDelta


def compute_delta(current: EvaluationRunResult, baseline: EvaluationRunResult) -> MetricDelta:
    return MetricDelta(
        groundedness_delta=current.groundedness - baseline.groundedness,
        faithfulness_delta=current.faithfulness - baseline.faithfulness,
        answer_relevance_delta=current.answer_relevance - baseline.answer_relevance,
        context_precision_delta=current.context_precision - baseline.context_precision,
    )


def has_regression(delta: MetricDelta, threshold: float) -> bool:
    return any(
        [
            delta.groundedness_delta < -threshold,
            delta.faithfulness_delta < -threshold,
            delta.answer_relevance_delta < -threshold,
            delta.context_precision_delta < -threshold,
        ]
    )

