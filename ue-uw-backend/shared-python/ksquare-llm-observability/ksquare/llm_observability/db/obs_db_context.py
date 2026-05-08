from __future__ import annotations

import json
from dataclasses import asdict
from datetime import date, datetime
from typing import Any, Optional

import aiosqlite

from ..models import CostSummary, EvaluationRunResult, MetricDelta


class ObsDbContext:
    def __init__(self, sqlite_path: str) -> None:
        self._sqlite_path = sqlite_path

    async def ensure_schema_async(self) -> None:
        async with aiosqlite.connect(self._sqlite_path) as db:
            await db.execute(
                """
                CREATE TABLE IF NOT EXISTS evaluation_runs (
                    run_id TEXT NOT NULL PRIMARY KEY,
                    run_name TEXT NOT NULL,
                    dataset_size INTEGER NOT NULL,
                    groundedness REAL NOT NULL,
                    faithfulness REAL NOT NULL,
                    answer_relevance REAL NOT NULL,
                    context_precision REAL NOT NULL,
                    context_recall REAL NOT NULL,
                    has_regression INTEGER NOT NULL DEFAULT 0,
                    vs_baseline_json TEXT NULL,
                    created_at TEXT NOT NULL
                )
                """
            )

            await db.execute(
                """
                CREATE TABLE IF NOT EXISTS llm_cost_daily (
                    cost_date TEXT NOT NULL PRIMARY KEY,
                    total_usd REAL NOT NULL,
                    prompt_tokens INTEGER NOT NULL,
                    completion_tokens INTEGER NOT NULL,
                    request_count INTEGER NOT NULL,
                    model_breakdown_json TEXT NULL,
                    user_breakdown_json TEXT NULL,
                    updated_at TEXT NOT NULL
                )
                """
            )

            await db.commit()

    def connect(self) -> aiosqlite.Connection:
        return aiosqlite.connect(self._sqlite_path)

    async def save_evaluation_run_async(self, run: EvaluationRunResult) -> None:
        await self.ensure_schema_async()
        baseline_json = json.dumps(asdict(run.vs_baseline)) if run.vs_baseline else None

        async with self.connect() as conn:
            await conn.execute(
                """
                INSERT INTO evaluation_runs (
                    run_id, run_name, dataset_size,
                    groundedness, faithfulness, answer_relevance,
                    context_precision, context_recall,
                    has_regression, vs_baseline_json, created_at
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    run.run_id,
                    run.run_name,
                    run.dataset_size,
                    float(run.groundedness),
                    float(run.faithfulness),
                    float(run.answer_relevance),
                    float(run.context_precision),
                    float(run.context_recall),
                    1 if run.has_regression else 0,
                    baseline_json,
                    run.created_at.isoformat(),
                ),
            )
            await conn.commit()

    async def get_latest_evaluation_run_async(self) -> Optional[EvaluationRunResult]:
        await self.ensure_schema_async()
        async with self.connect() as conn:
            cur = await conn.execute(
                """
                SELECT run_id, run_name, dataset_size,
                       groundedness, faithfulness, answer_relevance,
                       context_precision, context_recall,
                       has_regression, vs_baseline_json, created_at
                FROM evaluation_runs
                ORDER BY created_at DESC
                LIMIT 1
                """
            )
            row = await cur.fetchone()
            if row is None:
                return None

        vs_baseline = None
        if row[9]:
            d = json.loads(row[9])
            vs_baseline = MetricDelta(
                groundedness_delta=float(d["groundedness_delta"]),
                faithfulness_delta=float(d["faithfulness_delta"]),
                answer_relevance_delta=float(d["answer_relevance_delta"]),
                context_precision_delta=float(d["context_precision_delta"]),
            )

        return EvaluationRunResult(
            run_id=row[0],
            run_name=row[1],
            dataset_size=int(row[2]),
            groundedness=float(row[3]),
            faithfulness=float(row[4]),
            answer_relevance=float(row[5]),
            context_precision=float(row[6]),
            context_recall=float(row[7]),
            has_regression=bool(row[8]),
            vs_baseline=vs_baseline,
            created_at=datetime.fromisoformat(row[10]),
        )

    async def upsert_daily_cost_async(
        self,
        *,
        cost_date: date,
        total_usd: float,
        prompt_tokens: int,
        completion_tokens: int,
        request_count: int,
        model_breakdown: dict[str, float],
        user_breakdown: dict[str, float],
        updated_at: Optional[datetime] = None,
    ) -> None:
        await self.ensure_schema_async()
        updated_at = updated_at or datetime.utcnow()

        async with self.connect() as conn:
            await conn.execute(
                """
                INSERT INTO llm_cost_daily (
                    cost_date, total_usd, prompt_tokens, completion_tokens, request_count,
                    model_breakdown_json, user_breakdown_json, updated_at
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(cost_date) DO UPDATE SET
                    total_usd=excluded.total_usd,
                    prompt_tokens=excluded.prompt_tokens,
                    completion_tokens=excluded.completion_tokens,
                    request_count=excluded.request_count,
                    model_breakdown_json=excluded.model_breakdown_json,
                    user_breakdown_json=excluded.user_breakdown_json,
                    updated_at=excluded.updated_at
                """,
                (
                    cost_date.isoformat(),
                    float(total_usd),
                    int(prompt_tokens),
                    int(completion_tokens),
                    int(request_count),
                    json.dumps(model_breakdown),
                    json.dumps(user_breakdown),
                    updated_at.isoformat(),
                ),
            )
            await conn.commit()

    async def get_daily_cost_async(self, cost_date: date) -> Optional[CostSummary]:
        await self.ensure_schema_async()
        async with self.connect() as conn:
            cur = await conn.execute(
                """
                SELECT cost_date, total_usd, prompt_tokens, completion_tokens, request_count,
                       model_breakdown_json, user_breakdown_json
                FROM llm_cost_daily
                WHERE cost_date = ?
                """,
                (cost_date.isoformat(),),
            )
            row = await cur.fetchone()
            if row is None:
                return None

        model_breakdown = json.loads(row[5]) if row[5] else {}
        user_breakdown = json.loads(row[6]) if row[6] else {}
        total_tokens = int(row[2]) + int(row[3])
        requests = int(row[4])

        return CostSummary(
            period_start=date.fromisoformat(row[0]),
            period_end=date.fromisoformat(row[0]),
            total_usd=float(row[1]),
            prompt_tokens=int(row[2]),
            completion_tokens=int(row[3]),
            total_tokens=total_tokens,
            requests=requests,
            cost_by_model=model_breakdown,
            cost_by_user=user_breakdown,
            average_cost_per_request=float(row[1]) / max(requests, 1),
        )

    async def get_period_cost_async(self, from_date: date, to_date: date) -> CostSummary:
        await self.ensure_schema_async()
        async with self.connect() as conn:
            cur = await conn.execute(
                """
                SELECT cost_date, total_usd, prompt_tokens, completion_tokens, request_count,
                       model_breakdown_json, user_breakdown_json
                FROM llm_cost_daily
                WHERE cost_date >= ? AND cost_date <= ?
                ORDER BY cost_date ASC
                """,
                (from_date.isoformat(), to_date.isoformat()),
            )
            rows = await cur.fetchall()

        total_usd = 0.0
        prompt_tokens = 0
        completion_tokens = 0
        requests = 0
        model_costs: dict[str, float] = {}
        user_costs: dict[str, float] = {}

        for r in rows:
            total_usd += float(r[1])
            prompt_tokens += int(r[2])
            completion_tokens += int(r[3])
            requests += int(r[4])

            for k, v in (json.loads(r[5]) if r[5] else {}).items():
                model_costs[k] = model_costs.get(k, 0.0) + float(v)

            for k, v in (json.loads(r[6]) if r[6] else {}).items():
                user_costs[k] = user_costs.get(k, 0.0) + float(v)

        total_tokens = prompt_tokens + completion_tokens
        top_users = dict(sorted(user_costs.items(), key=lambda kv: kv[1], reverse=True)[:10])

        return CostSummary(
            period_start=from_date,
            period_end=to_date,
            total_usd=total_usd,
            prompt_tokens=prompt_tokens,
            completion_tokens=completion_tokens,
            total_tokens=total_tokens,
            requests=requests,
            cost_by_model=model_costs,
            cost_by_user=top_users,
            average_cost_per_request=total_usd / max(requests, 1),
        )

    async def fetch_positive_feedback_turns_async(self, min_date: datetime, limit: int) -> list[dict[str, Any]]:
        try:
            async with self.connect() as conn:
                cur = await conn.execute(
                    """
                    SELECT session_id, submission_id, content_redacted, created_at
                    FROM agent_conversation_turns
                    WHERE feedback_rating = 'positive'
                      AND role = 'assistant'
                      AND created_at >= ?
                    ORDER BY created_at DESC
                    LIMIT ?
                    """,
                    (min_date.isoformat(), limit),
                )
                rows = await cur.fetchall()
        except Exception:
            return []

        result = []
        for r in rows:
            result.append(
                {
                    "session_id": r[0],
                    "submission_id": r[1],
                    "content_redacted": r[2],
                    "created_at": r[3],
                }
            )
        return result

    async def fetch_cost_source_turns_async(self, from_dt: datetime, to_dt_exclusive: datetime) -> list[dict[str, Any]]:
        try:
            async with self.connect() as conn:
                cur = await conn.execute(
                    """
                    SELECT user_id, model_used, prompt_tokens, completion_tokens, estimated_cost_usd, created_at
                    FROM agent_conversation_turns
                    WHERE role = 'assistant'
                      AND created_at >= ?
                      AND created_at < ?
                    """,
                    (from_dt.isoformat(), to_dt_exclusive.isoformat()),
                )
                rows = await cur.fetchall()
        except Exception:
            return []

        result = []
        for r in rows:
            result.append(
                {
                    "user_id": r[0],
                    "model_used": r[1],
                    "prompt_tokens": r[2],
                    "completion_tokens": r[3],
                    "estimated_cost_usd": r[4],
                    "created_at": r[5],
                }
            )
        return result
