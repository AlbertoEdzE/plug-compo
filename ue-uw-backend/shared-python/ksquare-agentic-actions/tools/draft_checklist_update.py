from __future__ import annotations

import uuid
from datetime import datetime, timedelta, timezone

from contracts import ActionExecutionResult, DraftAction, DraftActionStatus, DraftActionType, IDraftActionHandler
from options import AgenticActionsOptions


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


class DraftChecklistUpdateHandler(IDraftActionHandler):
    def __init__(self, options: AgenticActionsOptions) -> None:
        self._options = options

    async def create_draft(self, submission_id: str, checklist_updates: list[dict]) -> DraftAction:
        if not submission_id:
            raise ValueError("submission_id is required")
        if not checklist_updates:
            raise ValueError("checklist_updates is required")

        created_at = _utcnow()
        expires_at = created_at + timedelta(minutes=self._options.draft_ttl_minutes)

        return DraftAction(
            draft_id=str(uuid.uuid4()),
            action_type=DraftActionType.UPDATE_CHECKLIST,
            submission_id=submission_id,
            status=DraftActionStatus.PENDING,
            preview_title="Update Checklist Items",
            preview_detail=f"Update {len(checklist_updates)} checklist item(s) after confirmation.",
            payload={"submission_id": submission_id, "checklist_updates": checklist_updates},
            requires_confirmation=True,
            created_at=created_at.isoformat(),
            expires_at=expires_at.isoformat(),
        )

    async def execute(self, draft: DraftAction) -> ActionExecutionResult:
        updates = list(draft.payload.get("checklist_updates", []) or [])
        return ActionExecutionResult(
            draft_id=draft.draft_id,
            action_type=DraftActionType.UPDATE_CHECKLIST,
            success=True,
            result_data={"updated_items": len(updates)},
        )
