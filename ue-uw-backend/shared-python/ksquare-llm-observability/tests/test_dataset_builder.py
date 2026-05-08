from datetime import datetime, timedelta

import pytest
import aiosqlite

from ksquare.llm_observability.dataset.dataset_builder import EvaluationDatasetBuilder
from ksquare.llm_observability.db.obs_db_context import ObsDbContext
from tests.synthesizers.eval_dataset_synthesizer import EvalDatasetSynthesizer


@pytest.mark.asyncio
async def test_dataset_builder_extracts_question_answer_and_tool_contexts(tmp_path):
    path = str(tmp_path / "obs.sqlite3")
    db = ObsDbContext(path)
    builder = EvaluationDatasetBuilder(db)
    synth = EvalDatasetSynthesizer()

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
                feedback_rating TEXT NULL,
                created_at TEXT NOT NULL
            )
            """
        )
        await conn.execute(
            """
            INSERT INTO agent_conversation_turns (
                turn_id, session_id, submission_id, user_id, role,
                content_hash, content_redacted, feedback_rating, created_at
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            ("t1", "s1", "sub1", "u1", "assistant", "h", synth.conversation_turn_json(), "positive", datetime.utcnow().isoformat()),
        )
        await conn.commit()

    min_date = datetime.utcnow() - timedelta(days=1)
    dataset = await builder.build_from_feedback_async(min_date=min_date, min_feedback_count=1)

    assert dataset.rows
    row = dataset.rows[0]
    assert "loss ratio" in row.question.lower()
    assert "loss ratio" in row.answer.lower()
    assert row.contexts

