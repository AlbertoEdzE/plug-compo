import pytest

from contracts import DraftActionStatus, DraftActionType
from options import AgenticActionsOptions
from tools.draft_referral import DraftReferralHandler


pytestmark = pytest.mark.asyncio


async def test_draft_referral_produces_pending_draft_with_required_fields() -> None:
    handler = DraftReferralHandler(AgenticActionsOptions(draft_ttl_minutes=10))
    draft = await handler.create_draft(
        submission_id="SUB-1234",
        referral_reason="TIV exceeds binding authority limit",
        priority="Normal",
        assigned_to_queue="SeniorUW-K12",
    )

    assert draft.status == DraftActionStatus.PENDING
    assert draft.requires_confirmation is True
    assert draft.action_type == DraftActionType.CREATE_REFERRAL
    assert draft.payload["submission_id"] == "SUB-1234"
    assert "reason" in draft.payload
