from __future__ import annotations

from dataclasses import dataclass
from datetime import date, datetime, time, timedelta
from typing import Optional

from ..config import LlmObservabilityConfig
from ..contracts import ICostTracker
from ..db.obs_db_context import ObsDbContext
from ..models import CostSummary


@dataclass(frozen=True)
class ModelPricing:
    input_per_1k: float
    output_per_1k: float


DEFAULT_PRICING: dict[str, ModelPricing] = {
    "gpt-4.1": ModelPricing(input_per_1k=0.002, output_per_1k=0.008),
    "gpt-4o": ModelPricing(input_per_1k=0.005, output_per_1k=0.015),
    "gpt-4o-mini": ModelPricing(input_per_1k=0.00015, output_per_1k=0.00060),
}


class CostTracker(ICostTracker):
    def __init__(self, db: ObsDbContext, config: LlmObservabilityConfig, pricing: Optional[dict[str, ModelPricing]] = None) -> None:
        self._db = db
        self._config = config
        self._pricing = pricing or DEFAULT_PRICING

    async def refresh_daily_cost_async(self, day: date) -> None:
        start = datetime.combine(day, time.min)
        next_day = start + timedelta(days=1)

        turns = await self._db.fetch_cost_source_turns_async(from_dt=start, to_dt_exclusive=next_day)

        total_usd = 0.0
        prompt_tokens = 0
        completion_tokens = 0
        request_count = 0
        by_model: dict[str, float] = {}
        by_user: dict[str, float] = {}

        for t in turns:
            request_count += 1
            model = (t.get("model_used") or "gpt-4.1").strip()
            user = t.get("user_id") or "unknown"

            p = int(t.get("prompt_tokens") or 0)
            c = int(t.get("completion_tokens") or 0)
            prompt_tokens += p
            completion_tokens += c

            cost = t.get("estimated_cost_usd")
            if cost is None:
                cost = self._estimate_cost(model, p, c)
            cost = float(cost)

            total_usd += cost
            by_model[model] = by_model.get(model, 0.0) + cost
            by_user[user] = by_user.get(user, 0.0) + cost

        await self._db.upsert_daily_cost_async(
            cost_date=day,
            total_usd=total_usd,
            prompt_tokens=prompt_tokens,
            completion_tokens=completion_tokens,
            request_count=request_count,
            model_breakdown=by_model,
            user_breakdown=dict(sorted(by_user.items(), key=lambda kv: kv[1], reverse=True)[:10]),
        )

    async def get_daily_cost_async(self, day: date) -> CostSummary:
        row = await self._db.get_daily_cost_async(day)
        return row or CostSummary(
            period_start=day,
            period_end=day,
            total_usd=0.0,
            prompt_tokens=0,
            completion_tokens=0,
            total_tokens=0,
            requests=0,
            cost_by_model={},
            cost_by_user={},
            average_cost_per_request=0.0,
        )

    async def get_period_cost_async(self, from_date: date, to_date: date) -> CostSummary:
        return await self._db.get_period_cost_async(from_date, to_date)

    def _estimate_cost(self, model: str, prompt_tokens: int, completion_tokens: int) -> float:
        rates = self._pricing.get(model, self._pricing["gpt-4.1"])
        return (prompt_tokens / 1000.0) * rates.input_per_1k + (completion_tokens / 1000.0) * rates.output_per_1k
