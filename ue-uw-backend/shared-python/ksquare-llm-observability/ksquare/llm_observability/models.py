from __future__ import annotations

from dataclasses import dataclass, field
from datetime import date, datetime
from typing import Optional


@dataclass
class EvaluationRow:
    question: str
    answer: str
    contexts: list[str]
    ground_truth: Optional[str] = None
    session_id: Optional[str] = None
    submission_id: Optional[str] = None
    prompt_version: Optional[str] = None


@dataclass
class EvaluationDataset:
    name: str
    rows: list[EvaluationRow]
    created_at: datetime = field(default_factory=datetime.utcnow)


@dataclass
class MetricDelta:
    groundedness_delta: float
    faithfulness_delta: float
    answer_relevance_delta: float
    context_precision_delta: float


@dataclass
class EvaluationRunResult:
    run_id: str
    run_name: str
    dataset_size: int
    groundedness: float
    faithfulness: float
    answer_relevance: float
    context_precision: float
    context_recall: float
    vs_baseline: Optional[MetricDelta] = None
    has_regression: bool = False
    created_at: datetime = field(default_factory=datetime.utcnow)


@dataclass
class CostSummary:
    period_start: date
    period_end: date
    total_usd: float
    prompt_tokens: int
    completion_tokens: int
    total_tokens: int
    requests: int
    cost_by_model: dict
    cost_by_user: dict
    average_cost_per_request: float


@dataclass
class LlmMetricsBatch:
    period_start: datetime
    period_end: datetime
    p50_latency_ms: float
    p95_latency_ms: float
    p99_latency_ms: float
    avg_groundedness: float
    avg_answer_relevance: float
    avg_context_relevance: float
    positive_feedback_rate: float
    negative_feedback_rate: float
    total_feedback_count: int
    safety_block_rate: float
    out_of_scope_rate: float
    total_requests: int
    total_cost_usd: float
    error_rate: float

