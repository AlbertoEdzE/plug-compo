from __future__ import annotations

from abc import ABC, abstractmethod
from typing import AsyncIterator

from .models import (
    AgentChatRequest,
    AgentStreamChunk,
    AssistantContext,
    ConversationTurn,
    EvaluationScores,
    SafetyCheckResult,
    ToolResult,
    UserContext,
    UserFeedback,
)


class IAgentOrchestrator(ABC):
    @abstractmethod
    async def chat_stream_async(self, request: AgentChatRequest) -> AsyncIterator[AgentStreamChunk]:
        ...


class IAssistantContextBuilder(ABC):
    @abstractmethod
    async def build_async(self, submission_id: str, user_context: UserContext) -> AssistantContext:
        ...


class IToolRouter(ABC):
    @abstractmethod
    async def execute_async(self, tool_name: str, arguments: dict, submission_id: str) -> ToolResult:
        ...


class ISafetyGuard(ABC):
    @abstractmethod
    async def check_input_async(self, text: str) -> SafetyCheckResult:
        ...

    @abstractmethod
    async def check_response_async(self, text: str, context: AssistantContext) -> SafetyCheckResult:
        ...


class IEvaluationScorer(ABC):
    @abstractmethod
    async def score_async(
        self,
        question: str,
        answer: str,
        context: str,
        retrieved_docs: list[str],
    ) -> EvaluationScores:
        ...


class IConversationAuditWriter(ABC):
    @abstractmethod
    async def write_turn_async(self, turn: ConversationTurn) -> None:
        ...

    @abstractmethod
    async def write_feedback_async(self, feedback: UserFeedback) -> None:
        ...

