import json
import os
from datetime import date, datetime, timedelta

import azure.functions as func

from ksquare.llm_observability.config import LlmObservabilityConfig
from ksquare.llm_observability.cost.cost_tracker import CostTracker
from ksquare.llm_observability.db.obs_db_context import ObsDbContext

app = func.FunctionApp()


def _db_path() -> str:
    return os.getenv("KSQUARE_LLM_OBS_SQLITE_PATH", "llm_observability.sqlite3")


@app.route(route="llm-obs/summary", methods=["GET"], auth_level=func.AuthLevel.FUNCTION)
async def llm_obs_summary(req: func.HttpRequest) -> func.HttpResponse:
    from_period = req.params.get("from", "7d")
    days = _parse_days(from_period)
    to_dt = datetime.utcnow()
    from_dt = to_dt - timedelta(days=days)

    db = ObsDbContext(_db_path())
    latest_eval = await db.get_latest_evaluation_run_async()
    costs = await db.get_period_cost_async(from_dt.date(), to_dt.date())

    result = {
        "period": from_period,
        "totalRequests": costs.requests,
        "avgGroundedness": (latest_eval.groundedness if latest_eval else 0.0),
        "avgAnswerRelevance": (latest_eval.answer_relevance if latest_eval else 0.0),
        "positiveFeedbackRate": 0.0,
        "p50LatencyMs": 0.0,
        "p95LatencyMs": 0.0,
        "totalCostUsd": costs.total_usd,
        "safetyBlockRate": 0.0,
        "errorRate": 0.0,
    }
    return func.HttpResponse(json.dumps(result), mimetype="application/json")


@app.route(route="llm-obs/evals/latest", methods=["GET"], auth_level=func.AuthLevel.FUNCTION)
async def llm_obs_evals_latest(req: func.HttpRequest) -> func.HttpResponse:
    db = ObsDbContext(_db_path())
    latest = await db.get_latest_evaluation_run_async()
    return func.HttpResponse(json.dumps(latest.__dict__ if latest else None, default=str), mimetype="application/json")


@app.route(route="llm-obs/costs/daily", methods=["GET"], auth_level=func.AuthLevel.FUNCTION)
async def llm_obs_costs_daily(req: func.HttpRequest) -> func.HttpResponse:
    days = int(req.params.get("days", "30"))
    to_day = date.today()
    from_day = to_day - timedelta(days=max(days - 1, 0))
    db = ObsDbContext(_db_path())
    summary = await db.get_period_cost_async(from_day, to_day)
    result = {
        "totalUsd": summary.total_usd,
        "avgDailyCost": summary.total_usd / max(days, 1),
    }
    return func.HttpResponse(json.dumps(result), mimetype="application/json")


@app.route(route="llm-obs/feedback/summary", methods=["GET"], auth_level=func.AuthLevel.FUNCTION)
async def llm_obs_feedback_summary(req: func.HttpRequest) -> func.HttpResponse:
    from_period = req.params.get("from", "30d")
    _ = _parse_days(from_period)
    result = {
        "totalFeedback": 0,
        "positive": 0,
        "negative": 0,
        "positiveRate": 0.0,
        "topNegativeTopics": [],
    }
    return func.HttpResponse(json.dumps(result), mimetype="application/json")


@app.route(route="llm-obs/costs/refresh", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def llm_obs_costs_refresh(req: func.HttpRequest) -> func.HttpResponse:
    body = req.get_json()
    d = date.fromisoformat(body["day"])
    cfg = LlmObservabilityConfig(connection_string=_db_path())
    db = ObsDbContext(_db_path())
    tracker = CostTracker(db, cfg)
    await tracker.refresh_daily_cost_async(d)
    return func.HttpResponse(status_code=204)


def _parse_days(value: str) -> int:
    v = (value or "7d").strip().lower()
    if v.endswith("d") and v[:-1].isdigit():
        return int(v[:-1])
    return 7
