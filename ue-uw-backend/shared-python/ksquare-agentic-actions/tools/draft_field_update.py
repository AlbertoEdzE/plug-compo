from __future__ import annotations

import uuid
from datetime import datetime, timedelta, timezone

from contracts import ActionExecutionResult, DraftAction, DraftActionStatus, DraftActionType, IDraftActionHandler
from options import AgenticActionsOptions


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


class DraftFieldUpdateHandler(IDraftActionHandler):
    def __init__(self, options: AgenticActionsOptions) -> None:
        self._options = options

    async def create_draft(self, submission_id: str, field_updates: list[dict]) -> DraftAction:
        if not submission_id:
            raise ValueError("submission_id is required")
        if not field_updates:
            raise ValueError("field_updates is required")

        created_at = _utcnow()
        expires_at = created_at + timedelta(minutes=self._options.draft_ttl_minutes)

        preview_title = "Update Submission Fields"
        preview_detail = f"Update {len(field_updates)} field(s) on the submission after confirmation."

        return DraftAction(
            draft_id=str(uuid.uuid4()),
            action_type=DraftActionType.UPDATE_FIELDS,
            submission_id=submission_id,
            status=DraftActionStatus.PENDING,
            preview_title=preview_title,
            preview_detail=preview_detail,
            payload={"submission_id": submission_id, "field_updates": field_updates},
            requires_confirmation=True,
            created_at=created_at.isoformat(),
            expires_at=expires_at.isoformat(),
        )

    async def execute(self, draft: DraftAction) -> ActionExecutionResult:
        updates = list(draft.payload.get("field_updates", []) or [])
        return ActionExecutionResult(
            draft_id=draft.draft_id,
            action_type=DraftActionType.UPDATE_FIELDS,
            success=True,
            result_data={"updated_fields": len(updates)},
        )
