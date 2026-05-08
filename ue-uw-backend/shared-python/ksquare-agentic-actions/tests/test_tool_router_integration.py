from __future__ import annotations

import sys
from pathlib import Path

import pytest

from draft_store import DraftStore
from event_bus import InMemoryEventPublisher
from options import AgenticActionsOptions
from tool_router_registration import register_agentic_actions
from tests.synthesizers.agentic_action_synthesizer import AgenticActionSynthesizer


pytestmark = pytest.mark.asyncio


def _add_agent_orchestrator_to_path() -> None:
    shared_python = Path(__file__).resolve().parents[2]
    orchestrator_dir = shared_python / "ksquare-agent-orchestrator"
    sys.path.insert(0, str(orchestrator_dir))


async def test_tool_router_can_execute_agentic_actions_and_publishes_event(tmp_path) -> None:
    _add_agent_orchestrator_to_path()

    from ksquare.agent_orchestrator.tools.tool_router import ToolRouter

    db_url = f"sqlite+aiosqlite:///{tmp_path / 'drafts.sqlite3'}"
    store = DraftStore(db_url)
    publisher = InMemoryEventPublisher()
    options = AgenticActionsOptions(enabled=True)

    router = ToolRouter()
    await register_agentic_actions(router, store, publisher, options)

    synth = AgenticActionSynthesizer(seed=42)
    submission_id = synth.submission_id()

    draft_result = await router.execute_async(
        "draft_referral",
        {"referral_reason": synth.referral_reason(), "priority": "Normal", "assigned_to_queue": "SeniorUW-K12"},
        submission_id=submission_id,
    )
    assert draft_result.success is True
    draft_id = str(draft_result.raw_data["draft_id"])

    await store.mark_confirmed_async(draft_id, confirmed_by="uw1")

    exec_result = await router.execute_async("execute_draft_action", {"draft_id": draft_id}, submission_id=submission_id)
    assert exec_result.success is True
    assert exec_result.raw_data["success"] is True
    assert len(publisher.events) == 1
    assert publisher.events[0].data["draft_id"] == draft_id
