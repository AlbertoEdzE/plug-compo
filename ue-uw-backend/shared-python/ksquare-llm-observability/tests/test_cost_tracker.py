from datetime import date, datetime

import pytest

import aiosqlite

from ksquare.llm_observability.config import LlmObservabilityConfig
from ksquare.llm_observability.cost.cost_tracker import CostTracker
from ksquare.llm_observability.db.obs_db_context import ObsDbContext


@pytest.mark.asyncio
async def test_cost_tracker_aggregates_turns_and_upserts_daily_cost(tmp_path):
    path = str(tmp_path / "obs.sqlite3")
    db = ObsDbContext(path)
    cfg = LlmObservabilityConfig(connection_string=path)
    tracker = CostTracker(db, cfg)

    async with aiosqlite.connect(path) as conn:
        await conn.execute(
            """
            CREATE TABLE agent_conversation_turns (
                turn_id TEXT NOT NULL PRIMARY KEY,
                session_id TEXT NOT NULL,
                submission_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                content_redacted TEXT NOT NULL,
                model_used TEXT NULL,
                prompt_tokens INTEGER NULL,
                completion_tokens INTEGER NULL,
                latency_ms INTEGER NULL,
                finish_reason TEXT NULL,
                tool_calls_json TEXT NULL,
                eval_groundedness REAL NULL,
                eval_answer_relevance REAL NULL,
                eval_context_relevance REAL NULL,
                feedback_rating TEXT NULL,
                feedback_comment TEXT NULL,
                feedback_at TEXT NULL,
                estimated_cost_usd REAL NULL,
                created_at TEXT NOT NULL
            )
            """
        )
        now = datetime.utcnow().isoformat()
        day = date.fromisoformat(now.split("T")[0])
        await conn.execute(
            """
            INSERT INTO agent_conversation_turns (
                turn_id, session_id, submission_id, user_id, role, content_hash, content_redacted,
                model_used, prompt_tokens, completion_tokens, estimated_cost_usd, created_at
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            ("t1", "s1", "sub1", "u1", "assistant", "h", "ok", "gpt-4o-mini", 1000, 500, 0.10, now),
        )
        await conn.execute(
            """
            INSERT INTO agent_conversation_turns (
                turn_id, session_id, submission_id, user_id, role, content_hash, content_redacted,
                model_used, prompt_tokens, completion_tokens, estimated_cost_usd, created_at
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            ("t2", "s1", "sub1", "u2", "assistant", "h", "ok", "gpt-4o-mini", 2000, 0, 0.20, now),
        )
        await conn.commit()

    await tracker.refresh_daily_cost_async(day)
    summary = await tracker.get_daily_cost_async(day)

    assert summary.requests == 2
    assert summary.total_usd == pytest.approx(0.30, rel=1e-6)
    assert summary.prompt_tokens == 3000
    assert summary.completion_tokens == 500
    assert summary.cost_by_model["gpt-4o-mini"] == pytest.approx(0.30, rel=1e-6)
