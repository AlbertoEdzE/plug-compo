import pytest

from ksquare.agent_orchestrator.tools.tool_router import ToolRouter


@pytest.mark.asyncio
async def test_tool_router_dispatches_to_correct_tool():
    router = ToolRouter()
    result = await router.execute_async("get_loss_history", {"years": 3}, submission_id="SUB-1234")
    assert result.success is True
    assert result.raw_data is not None
    assert result.raw_data["years"] == 3
    assert len(result.raw_data["history"]) == 3

