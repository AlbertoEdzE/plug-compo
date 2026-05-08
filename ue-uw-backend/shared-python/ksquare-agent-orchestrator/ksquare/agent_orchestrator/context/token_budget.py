from __future__ import annotations

from ..models import ChatMessage


def trim_messages_to_budget(
    messages: list[ChatMessage],
    system_reserved_tokens: int,
    max_context_tokens: int,
) -> list[ChatMessage]:
    max_non_system = max(max_context_tokens - system_reserved_tokens, 0)

    system_messages = [m for m in messages if m.role == "system"]
    other_messages = [m for m in messages if m.role != "system"]

    while estimate_tokens(other_messages) > max_non_system and other_messages:
        other_messages.pop(0)

    return [*system_messages, *other_messages]


def estimate_tokens(messages: list[ChatMessage]) -> int:
    total_chars = sum(len(m.content or "") for m in messages)
    return max(total_chars // 4, 1) if messages else 0

