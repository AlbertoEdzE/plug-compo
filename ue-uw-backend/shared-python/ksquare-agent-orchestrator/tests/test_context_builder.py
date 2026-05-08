import pytest

from ksquare.agent_orchestrator.context.context_builder import AssistantContextBuilder
from ksquare.agent_orchestrator.models import UserContext


@pytest.mark.asyncio
async def test_context_builder_builds_formatted_context_block_contains_institution_and_status():
    builder = AssistantContextBuilder()
    ctx = await builder.build_async("SUB-1234", UserContext(user_id="u1", user_role="UNDERWRITER", display_name="User 1"))
    assert "InstitutionName" in ctx.formatted_context_block
    assert "Status" in ctx.formatted_context_block

