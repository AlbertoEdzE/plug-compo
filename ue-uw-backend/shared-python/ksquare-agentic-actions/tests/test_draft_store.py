from __future__ import annotations

from datetime import datetime, timedelta, timezone

import pytest

from contracts import DraftAction, DraftActionStatus, DraftActionType
from draft_store import DraftExpiredException, DraftNotConfirmedException, DraftStore
from event_bus import InMemoryEventPublisher
from executor import ActionExecutor
from options import AgenticActionsOptions
from tests.synthesizers.agentic_action_synthesizer import AgenticActionSynthesizer
from tools.draft_referral import DraftReferralHandler


pytestmark = pytest.mark.asyncio


async def test_execute_on_pending_draft_raises_not_confirmed(tmp_path) -> None:
    db_url = f"sqlite+aiosqlite:///{tmp_path / 'drafts.sqlite3'}"
    store = DraftStore(db_url)
    options = AgenticActionsOptions()

    synth = AgenticActionSynthesizer(seed=1)
    handler = DraftReferralHandler(options)
    draft = await handler.create_draft(submission_id=synth.submission_id(), referral_reason=synth.referral_reason())
    await store.save_async(draft)

    publisher = InMemoryEventPublisher()
    executor = ActionExecutor(store, publisher, handlers={DraftActionType.CREATE_REFERRAL: handler})

    with pytest.raises(DraftNotConfirmedException):
        await executor.execute_async(draft)


async def test_expired_draft_is_rejected_on_execute(tmp_path) -> None:
    db_url = f"sqlite+aiosqlite:///{tmp_path / 'drafts.sqlite3'}"
    store = DraftStore(db_url)

    now = datetime.now(timezone.utc)
    expired = DraftAction(
        draft_id="draft-expired",
        action_type=DraftActionType.CREATE_REFERRAL,
        submission_id="SUB-1",
        status=DraftActionStatus.CONFIRMED,
        preview_title="x",
        preview_detail="y",
        payload={"submission_id": "SUB-1"},
        requires_confirmation=True,
        created_at=now.isoformat(),
        expires_at=(now - timedelta(minutes=1)).isoformat(),
    )
    await store.save_async(expired)

    with pytest.raises(DraftExpiredException):
        await store.ensure_confirmed_for_execute_async("draft-expired")


async def test_execute_is_idempotent_and_publishes_single_event(tmp_path) -> None:
    db_url = f"sqlite+aiosqlite:///{tmp_path / 'drafts.sqlite3'}"
    store = DraftStore(db_url)
    options = AgenticActionsOptions()

    synth = AgenticActionSynthesizer(seed=2)
    handler = DraftReferralHandler(options)
    draft = await handler.create_draft(submission_id=synth.submission_id(), referral_reason=synth.referral_reason())
    await store.save_async(draft)
    await store.mark_confirmed_async(draft.draft_id, confirmed_by="uw1")

    publisher = InMemoryEventPublisher()
    executor = ActionExecutor(store, publisher, handlers={DraftActionType.CREATE_REFERRAL: handler})

    confirmed = await store.ensure_confirmed_for_execute_async(draft.draft_id)
    r1 = await executor.execute_async(confirmed)
    r2 = await executor.execute_async(confirmed)

    assert r1.success is True
    assert r2.draft_id == r1.draft_id
    assert len(publisher.events) == 1
