from __future__ import annotations

import uuid
from datetime import datetime, timedelta, timezone

from contracts import ActionExecutionResult, DraftAction, DraftActionStatus, DraftActionType, IDraftActionHandler
from options import AgenticActionsOptions


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


class DraftInfoRequestHandler(IDraftActionHandler):
    def __init__(self, options: AgenticActionsOptions) -> None:
        self._options = options

    async def create_draft(
        self,
        submission_id: str,
        broker_email: str,
        requested_items: list[str],
        due_date: str | None = None,
        custom_message: str | None = None,
    ) -> DraftAction:
        if not submission_id:
            raise ValueError("submission_id is required")
        if not broker_email:
            raise ValueError("broker_email is required")
        if not requested_items:
            raise ValueError("requested_items is required")

        created_at = _utcnow()
        expires_at = created_at + timedelta(minutes=self._options.draft_ttl_minutes)

        items_preview = "; ".join(requested_items[:3])
        if len(requested_items) > 3:
            items_preview += f" (+{len(requested_items) - 3} more)"

        return DraftAction(
            draft_id=str(uuid.uuid4()),
            action_type=DraftActionType.SEND_INFO_REQUEST,
            submission_id=submission_id,
            status=DraftActionStatus.PENDING,
            preview_title="Send Information Request to Broker",
            preview_detail=f"Email to {broker_email} requesting: {items_preview}",
            payload={
                "submission_id": submission_id,
                "broker_email": broker_email,
                "requested_items": requested_items,
                "due_date": due_date,
                "custom_message": custom_message,
            },
            requires_confirmation=True,
            created_at=created_at.isoformat(),
            expires_at=expires_at.isoformat(),
        )

    async def execute(self, draft: DraftAction) -> ActionExecutionResult:
        return ActionExecutionResult(
            draft_id=draft.draft_id,
            action_type=DraftActionType.SEND_INFO_REQUEST,
            success=True,
            result_data={"sent": True},
        )
