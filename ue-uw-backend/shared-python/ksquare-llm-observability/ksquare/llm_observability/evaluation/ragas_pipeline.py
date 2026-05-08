from __future__ import annotations

import re
import uuid
from datetime import datetime
from typing import Optional

from ..config import LlmObservabilityConfig
from ..contracts import IEvaluationPipeline
from ..db.obs_db_context import ObsDbContext
from ..models import EvaluationDataset, EvaluationRunResult
from .regression_detector import compute_delta, has_regression


class RagasEvaluationPipeline(IEvaluationPipeline):
    def __init__(self, db: ObsDbContext, config: LlmObservabilityConfig) -> None:
        self._db = db
        self._config = config

    async def run_offline_evaluation_async(self, dataset: EvaluationDataset, run_name: Optional[str] = None) -> EvaluationRunResult:
        baseline = await self._db.get_latest_evaluation_run_async()

        metrics = _evaluate_dataset_heuristic(dataset)
        run = EvaluationRunResult(
            run_id=uuid.uuid4().hex,
            run_name=run_name or f"eval_{datetime.utcnow().strftime('%Y%m%d_%H%M%S')}",
            dataset_size=len(dataset.rows),
            groundedness=metrics["groundedness"],
            faithfulness=metrics["faithfulness"],
            answer_relevance=metrics["answer_relevance"],
            context_precision=metrics["context_precision"],
            context_recall=metrics["context_recall"],
        )

        if baseline is not None:
            delta = compute_delta(run, baseline)
            run.vs_baseline = delta
            run.has_regression = has_regression(delta, self._config.regression_threshold)

        await self._db.save_evaluation_run_async(run)
        return run


def _evaluate_dataset_heuristic(dataset: EvaluationDataset) -> dict[str, float]:
    if not dataset.rows:
        return {
            "groundedness": 0.0,
            "faithfulness": 0.0,
            "answer_relevance": 0.0,
            "context_precision": 0.0,
            "context_recall": 0.0,
        }

    groundedness = []
    faithfulness = []
    answer_relevance = []
    context_precision = []
    context_recall = []

    for row in dataset.rows:
        contexts_text = "\n".join(row.contexts or [])
        groundedness.append(_score_numbers_supported(row.answer, contexts_text))
        faithfulness.append(_score_claim_terms_supported(row.answer, contexts_text))
        answer_relevance.append(_score_keyword_overlap(row.question, row.answer))
        context_precision.append(_score_context_precision(row.question, row.contexts))
        context_recall.append(_score_context_recall(row.ground_truth, row.contexts))

    return {
        "groundedness": float(sum(groundedness) / len(groundedness)),
        "faithfulness": float(sum(faithfulness) / len(faithfulness)),
        "answer_relevance": float(sum(answer_relevance) / len(answer_relevance)),
        "context_precision": float(sum(context_precision) / len(context_precision)),
        "context_recall": float(sum(context_recall) / len(context_recall)),
    }


_NUM_RE = re.compile(r"\b\d+(?:\.\d+)?\b")
_WORD_RE = re.compile(r"[a-z]{4,}")


def _score_numbers_supported(answer: str, context: str) -> float:
    a_nums = set(_NUM_RE.findall((answer or "").lower()))
    c_nums = set(_NUM_RE.findall((context or "").lower()))
    if not a_nums:
        return 0.8
    if a_nums.issubset(c_nums):
        return 1.0
    return 0.2


def _score_claim_terms_supported(answer: str, context: str) -> float:
    a_terms = {w for w in _WORD_RE.findall((answer or "").lower())}
    c_terms = {w for w in _WORD_RE.findall((context or "").lower())}
    if not a_terms:
        return 0.5
    overlap = len(a_terms & c_terms) / max(len(a_terms), 1)
    return min(0.4 + overlap, 1.0)


def _score_keyword_overlap(question: str, answer: str) -> float:
    stop = {"the", "a", "is", "what", "how", "why", "when", "and", "or", "to", "of"}
    q_words = {w for w in (question or "").lower().split() if w not in stop}
    if not q_words:
        return 0.5
    a_lower = (answer or "").lower()
    matched = sum(1 for w in q_words if w in a_lower)
    return min(matched / max(len(q_words), 1), 1.0)


def _score_context_precision(question: str, contexts: list[str]) -> float:
    if not contexts:
        return 0.0
    stop = {"the", "a", "is", "what", "how", "why", "when", "and", "or", "to", "of"}
    q_words = {w for w in (question or "").lower().split() if w not in stop}
    if not q_words:
        return 0.0
    relevant = sum(1 for ctx in contexts if any(w in (ctx or "").lower() for w in q_words))
    return relevant / len(contexts)


def _score_context_recall(ground_truth: Optional[str], contexts: list[str]) -> float:
    if not ground_truth:
        return 0.0
    gt_terms = {w for w in _WORD_RE.findall(ground_truth.lower())}
    if not gt_terms:
        return 0.0
    ctx_terms = {w for w in _WORD_RE.findall("\n".join(contexts or []).lower())}
    overlap = len(gt_terms & ctx_terms) / max(len(gt_terms), 1)
    return min(overlap, 1.0)

