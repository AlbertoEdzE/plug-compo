from __future__ import annotations

import uuid

from faker import Faker

from ksquare.agent_orchestrator.models import AgentChatRequest, ChatMessage


class AgentSynthesizer:
    def __init__(self, seed: int = 1337) -> None:
        self._faker = Faker()
        Faker.seed(seed)

    def chat_request(self, *, submission_id: str | None = None, session_id: str | None = None, user_id: str | None = None) -> AgentChatRequest:
        return AgentChatRequest(
            session_id=session_id or uuid.uuid4().hex,
            submission_id=submission_id or f"SUB-{self._faker.random_int(min=1000, max=9999)}",
            user_id=user_id or self._faker.user_name(),
            user_role="UNDERWRITER",
            messages=[
                ChatMessage(role="user", content=self._faker.sentence()),
            ],
            correlation_id=uuid.uuid4().hex,
        )

