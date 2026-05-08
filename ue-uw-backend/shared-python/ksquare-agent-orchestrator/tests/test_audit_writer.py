import json

import pytest

from ksquare.agent_orchestrator.audit.audit_db_context import AuditDbContext
from ksquare.agent_orchestrator.audit.conversation_audit_writer import SqliteConversationAuditWriter
from ksquare.agent_orchestrator.models import ConversationTurn, EvaluationScores, UserFeedback


@pytest.mark.asyncio
async def test_conversation_audit_writer_redacts_email(tmp_path):
    db = AuditDbContext(str(tmp_path / "audit.sqlite3"))
    writer = SqliteConversationAuditWriter(db)

    turn = ConversationTurn(
        turn_id="t1",
        session_id="s1",
        submission_id="sub1",
        user_id="u1",
        role="assistant",
        content_hash="",
        content_redacted="Contact me at test@example.com",
        model_used="gpt-4.1",
        prompt_tokens=10,
        completion_tokens=10,
        latency_ms=1,
        finish_reason="stop",
        tool_calls=[],
        eval_scores=EvaluationScores(groundedness=1.0, answer_relevance=1.0, context_relevance=1.0),
    )

    await writer.write_turn_async(turn)

    async with db.connect() as conn:
        cur = await conn.execute("SELECT content_redacted FROM agent_conversation_turns WHERE turn_id = ?", ("t1",))
        row = await cur.fetchone()
        assert row is not None
        assert "REDACTED" in row[0]
        assert "test@example.com" not in row[0]


@pytest.mark.asyncio
async def test_feedback_handler_updates_rating(tmp_path):
    db = AuditDbContext(str(tmp_path / "audit.sqlite3"))
    writer = SqliteConversationAuditWriter(db)

    turn = ConversationTurn(
        turn_id="t1",
        session_id="s1",
        submission_id="sub1",
        user_id="u1",
        role="assistant",
        content_hash="",
        content_redacted="ok",
        model_used="gpt-4.1",
        prompt_tokens=1,
        completion_tokens=1,
        latency_ms=1,
        finish_reason="stop",
        tool_calls=[{"tool_name": "x"}],
        eval_scores=EvaluationScores(),
    )
    await writer.write_turn_async(turn)

    await writer.write_feedback_async(UserFeedback(session_id="s1", turn_id="t1", user_id="u1", rating="positive", comment="good"))

    async with db.connect() as conn:
        cur = await conn.execute(
            "SELECT feedback_rating, feedback_comment FROM agent_conversation_turns WHERE turn_id = ? AND session_id = ?",
            ("t1", "s1"),
        )
        row = await cur.fetchone()
        assert row is not None
        assert row[0] == "positive"
        assert row[1] == "good"
