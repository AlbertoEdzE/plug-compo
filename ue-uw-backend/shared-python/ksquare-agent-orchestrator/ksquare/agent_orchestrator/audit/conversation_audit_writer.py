from __future__ import annotations

import hashlib
import json

from ..contracts import IConversationAuditWriter
from ..models import ConversationTurn, UserFeedback
from .audit_db_context import AuditDbContext
from .pii_redactor import redact_pii


class SqliteConversationAuditWriter(IConversationAuditWriter):
    def __init__(self, db: AuditDbContext) -> None:
        self._db = db

    async def write_turn_async(self, turn: ConversationTurn) -> None:
        await self._db.ensure_schema_async()
        redacted = redact_pii(turn.content_redacted)
        content_hash = hashlib.sha256(redacted.encode("utf-8")).hexdigest()

        async with self._db.connect() as conn:
            await conn.execute(
                """
                INSERT INTO agent_conversation_turns (
                    turn_id, session_id, submission_id, user_id, role,
                    content_hash, content_redacted, model_used,
                    prompt_tokens, completion_tokens, latency_ms, finish_reason,
                    tool_calls_json, eval_groundedness, eval_answer_relevance, eval_context_relevance,
                    estimated_cost_usd, created_at
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    turn.turn_id,
                    turn.session_id,
                    turn.submission_id,
                    turn.user_id,
                    turn.role,
                    content_hash,
                    redacted,
                    turn.model_used,
                    turn.prompt_tokens,
                    turn.completion_tokens,
                    turn.latency_ms,
                    turn.finish_reason,
                    json.dumps(turn.tool_calls),
                    (turn.eval_scores.groundedness if turn.eval_scores else None),
                    (turn.eval_scores.answer_relevance if turn.eval_scores else None),
                    (turn.eval_scores.context_relevance if turn.eval_scores else None),
                    (turn.eval_scores.estimated_cost_usd if turn.eval_scores else None),
                    turn.created_at.isoformat(),
                ),
            )
            await conn.commit()

    async def write_feedback_async(self, feedback: UserFeedback) -> None:
        await self._db.ensure_schema_async()
        async with self._db.connect() as conn:
            await conn.execute(
                """
                UPDATE agent_conversation_turns
                SET feedback_rating = ?,
                    feedback_comment = ?,
                    feedback_at = ?
                WHERE turn_id = ? AND session_id = ?
                """,
                (
                    feedback.rating,
                    feedback.comment,
                    feedback.created_at.isoformat(),
                    feedback.turn_id,
                    feedback.session_id,
                ),
            )
            await conn.commit()
