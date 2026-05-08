from __future__ import annotations

from typing import AsyncIterator

from .config import AgentOrchestratorConfig
from .contracts import IAgentOrchestrator
from .models import AgentChatRequest, AgentStreamChunk


class AgentOrchestrator(IAgentOrchestrator):
    def __init__(self, config: AgentOrchestratorConfig) -> None:
        self._config = config

    async def chat_stream_async(self, request: AgentChatRequest) -> AsyncIterator[AgentStreamChunk]:
        yield AgentStreamChunk(delta="", is_final=True, error="AgentOrchestrator not implemented.")

