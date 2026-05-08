from datetime import date, datetime

import pytest
import httpx

from ksquare.llm_observability.api.dashboard_api import create_app
from ksquare.llm_observability.db.obs_db_context import ObsDbContext
from ksquare.llm_observability.models import EvaluationRunResult


@pytest.mark.asyncio
async def test_dashboard_summary_uses_latest_eval_and_costs(tmp_path):
    path = str(tmp_path / "obs.sqlite3")
    db = ObsDbContext(path)

    await db.save_evaluation_run_async(
        EvaluationRunResult(
            run_id="r1",
            run_name="nightly",
            dataset_size=10,
            groundedness=0.9,
            faithfulness=0.9,
            answer_relevance=0.8,
            context_precision=0.7,
            context_recall=0.6,
        )
    )

    await db.upsert_daily_cost_async(
        cost_date=date.today(),
        total_usd=1.25,
        prompt_tokens=1000,
        completion_tokens=500,
        request_count=10,
        model_breakdown={"gpt-4.1": 1.25},
        user_breakdown={"u1": 1.25},
        updated_at=datetime.utcnow(),
    )

    app = create_app(path)
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.get("/api/llm-obs/summary?from=7d")
        resp.raise_for_status()
        payload = resp.json()

    assert payload["totalRequests"] == 10
    assert payload["avgGroundedness"] == pytest.approx(0.9, rel=1e-6)
    assert payload["totalCostUsd"] == pytest.approx(1.25, rel=1e-6)
