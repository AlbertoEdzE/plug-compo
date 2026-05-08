from __future__ import annotations

import uuid
from datetime import datetime, timedelta, timezone
from typing import Optional

from contracts import ActionExecutionResult, DraftAction, DraftActionStatus, DraftActionType, IDraftActionHandler
from options import AgenticActionsOptions


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


class DraftReferralHandler(IDraftActionHandler):
    def __init__(self, options: AgenticActionsOptions) -> None:
        self._options = options

    async def create_draft(
        self,
        submission_id: str,
        referral_reason: str,
        priority: str = "Normal",
        assigned_to_queue: str = "SeniorUW",
    ) -> DraftAction:
        if not submission_id:
            raise ValueError("submission_id is required")

        created_at = _utcnow()
        expires_at = created_at + timedelta(minutes=self._options.draft_ttl_minutes)

        preview_detail = (
            f"Create referral to {assigned_to_queue} queue with {priority} priority. "
            f"Reason: {referral_reason[:100]}"
        )

        return DraftAction(
            draft_id=str(uuid.uuid4()),
            action_type=DraftActionType.CREATE_REFERRAL,
            submission_id=submission_id,
            status=DraftActionStatus.PENDING,
            preview_title=f"Create Referral — {priority} Priority",
            preview_detail=preview_detail,
            payload={
                "submission_id": submission_id,
                "reason": referral_reason,
                "priority": priority,
                "queue": assigned_to_queue,
            },
            requires_confirmation=True,
            created_at=created_at.isoformat(),
            expires_at=expires_at.isoformat(),
        )

    async def execute(self, draft: DraftAction) -> ActionExecutionResult:
        return ActionExecutionResult(
            draft_id=draft.draft_id,
            action_type=DraftActionType.CREATE_REFERRAL,
            success=True,
            result_data={"referral_id": f"REF-{uuid.uuid4().hex[:8].upper()}"},
        )
