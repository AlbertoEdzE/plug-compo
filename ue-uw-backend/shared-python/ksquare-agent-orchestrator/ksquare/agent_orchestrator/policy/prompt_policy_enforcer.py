from __future__ import annotations

from .system_prompt import SYSTEM_PROMPT
from ..models import AssistantContext, ChatMessage, UserContext


class PromptPolicyEnforcer:
    def enforce(self, messages: list[ChatMessage], context: AssistantContext, user_context: UserContext) -> list[ChatMessage]:
        system_content = SYSTEM_PROMPT.format(
            user_role=user_context.user_role,
            user_display_name=user_context.display_name,
            submission_number=context.submission_number,
            institution_name=context.institution_name,
            submission_context_block=context.formatted_context_block,
        )

        system_message = ChatMessage(role="system", content=system_content)
        return [system_message, *messages]

