import pytest

from ksquare.agent_orchestrator.config import AgentOrchestratorConfig
from ksquare.agent_orchestrator.orchestrator import AgentOrchestrator
from ksquare.agent_orchestrator.models import AgentChatRequest, ChatMessage


@pytest.mark.asyncio
async def test_orchestrator_calls_get_loss_history_when_user_asks_loss_ratio(tmp_path):
    cfg = AgentOrchestratorConfig(enable_safety_check=False)
    orch = AgentOrchestrator(cfg, audit_sqlite_path=str(tmp_path / "audit.sqlite3"))
    req = AgentChatRequest(
        session_id="s1",
        submission_id="SUB-1234",
        user_id="u1",
        user_role="UNDERWRITER",
        messages=[ChatMessage(role="user", content="What is the loss ratio?")],
    )

    chunks = []
    async for c in orch.chat_stream_async(req):
        chunks.append(c)

    tool_chunks = [c for c in chunks if c.tool_call is not None]
    assert any(tc.tool_call.tool_name == "get_loss_history" for tc in tool_chunks)
