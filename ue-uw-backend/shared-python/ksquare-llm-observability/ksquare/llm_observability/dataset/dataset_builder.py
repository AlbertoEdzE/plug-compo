from __future__ import annotations

import json
from datetime import datetime

from ..db.obs_db_context import ObsDbContext
from ..models import EvaluationDataset, EvaluationRow


class EvaluationDatasetBuilder:
    def __init__(self, db: ObsDbContext) -> None:
        self._db = db

    async def build_from_feedback_async(self, min_date: datetime, min_feedback_count: int = 50) -> EvaluationDataset:
        rows_db = await self._db.fetch_positive_feedback_turns_async(min_date=min_date, limit=min_feedback_count * 3)

        rows: list[EvaluationRow] = []
        for db_row in rows_db:
            try:
                content = json.loads(db_row["content_redacted"])
            except Exception:
                continue

            user_turn = next((m for m in reversed(content) if m.get("role") == "user"), None)
            asst_turn = next((m for m in reversed(content) if m.get("role") == "assistant"), None)
            tool_contexts = [m.get("content", "") for m in content if m.get("role") == "tool"]

            if user_turn and asst_turn:
                rows.append(
                    EvaluationRow(
                        question=user_turn.get("content", ""),
                        answer=asst_turn.get("content", ""),
                        contexts=tool_contexts,
                        session_id=db_row.get("session_id"),
                        submission_id=db_row.get("submission_id"),
                    )
                )

        return EvaluationDataset(
            name=f"feedback_dataset_{datetime.utcnow().strftime('%Y%m%d')}",
            rows=rows[:min_feedback_count],
        )

