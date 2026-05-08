from __future__ import annotations

from dataclasses import asdict
from datetime import date, datetime, timedelta

from fastapi import FastAPI, Query

from ..config import LlmObservabilityConfig
from ..cost.cost_tracker import CostTracker
from ..db.obs_db_context import ObsDbContext
from ..models import LlmMetricsBatch


def create_app(db_path: str, config: LlmObservabilityConfig | None = None) -> FastAPI:
    cfg = config or LlmObservabilityConfig(connection_string=db_path)
    db = ObsDbContext(db_path)
    cost = CostTracker(db, cfg)

    app = FastAPI()

    @app.get("/api/llm-obs/summary")
    async def summary(from_period: str = Query("7d", alias="from")):
        days = _parse_days(from_period)
        to_dt = datetime.utcnow()
        from_dt = to_dt - timedelta(days=days)

        latest_eval = await db.get_latest_evaluation_run_async()
        costs = await db.get_period_cost_async(from_dt.date(), to_dt.date())

        positive = 0
        negative = 0
        total_feedback = positive + negative
        positive_rate = (positive / total_feedback) if total_feedback else 0.0

        metrics = LlmMetricsBatch(
            period_start=from_dt,
            period_end=to_dt,
            p50_latency_ms=0.0,
            p95_latency_ms=0.0,
            p99_latency_ms=0.0,
            avg_groundedness=(latest_eval.groundedness if latest_eval else 0.0),
            avg_answer_relevance=(latest_eval.answer_relevance if latest_eval else 0.0),
            avg_context_relevance=(latest_eval.context_precision if latest_eval else 0.0),
            positive_feedback_rate=positive_rate,
            negative_feedback_rate=0.0,
            total_feedback_count=total_feedback,
            safety_block_rate=0.0,
            out_of_scope_rate=0.0,
            total_requests=costs.requests,
            total_cost_usd=costs.total_usd,
            error_rate=0.0,
        )

        return {
            "period": from_period,
            "totalRequests": metrics.total_requests,
            "avgGroundedness": metrics.avg_groundedness,
            "avgAnswerRelevance": metrics.avg_answer_relevance,
            "positiveFeedbackRate": metrics.positive_feedback_rate,
            "p50LatencyMs": metrics.p50_latency_ms,
            "p95LatencyMs": metrics.p95_latency_ms,
            "totalCostUsd": metrics.total_cost_usd,
            "safetyBlockRate": metrics.safety_block_rate,
            "errorRate": metrics.error_rate,
        }

    @app.get("/api/llm-obs/evals/latest")
    async def evals_latest():
        latest = await db.get_latest_evaluation_run_async()
        if latest is None:
            return None
        payload = asdict(latest)
        return payload

    @app.get("/api/llm-obs/costs/daily")
    async def costs_daily(days: int = 30):
        to_day = date.today()
        from_day = to_day - timedelta(days=max(days - 1, 0))
        summary = await db.get_period_cost_async(from_day, to_day)
        return {
            "totalUsd": summary.total_usd,
            "avgDailyCost": summary.total_usd / max(days, 1),
        }

    @app.get("/api/llm-obs/feedback/summary")
    async def feedback_summary(from_period: str = Query("30d", alias="from")):
        _ = _parse_days(from_period)
        positive = 0
        negative = 0
        total = positive + negative
        return {
            "totalFeedback": total,
            "positive": positive,
            "negative": negative,
            "positiveRate": (positive / total) if total else 0.0,
            "topNegativeTopics": [],
        }

    @app.post("/api/llm-obs/costs/refresh")
    async def refresh_costs(day: str):
        d = date.fromisoformat(day)
        await cost.refresh_daily_cost_async(d)
        return {"status": "ok"}

    return app


def _parse_days(value: str) -> int:
    v = (value or "7d").strip().lower()
    if v.endswith("d") and v[:-1].isdigit():
        return int(v[:-1])
    return 7

