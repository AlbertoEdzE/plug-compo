from __future__ import annotations

import aiosqlite


class AuditDbContext:
    def __init__(self, sqlite_path: str) -> None:
        if sqlite_path == ":memory:":
            self._sqlite_path = "file:ksquare_agent_audit?mode=memory&cache=shared"
            self._uri = True
        else:
            self._sqlite_path = sqlite_path
            self._uri = False

    async def ensure_schema_async(self) -> None:
        async with aiosqlite.connect(self._sqlite_path, uri=self._uri) as db:
            await db.execute(
                """
                CREATE TABLE IF NOT EXISTS agent_conversation_turns (
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
            await db.commit()

    def connect(self) -> aiosqlite.Connection:
        return aiosqlite.connect(self._sqlite_path, uri=self._uri)
