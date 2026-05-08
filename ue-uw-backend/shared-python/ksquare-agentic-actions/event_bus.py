from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any


@dataclass(frozen=True)
class PublishedEvent:
    event_type: str
    data: dict[str, Any]
    published_at: str


class IEventPublisher(ABC):
    @abstractmethod
    async def publish_async(self, event_type: str, data: dict[str, Any]) -> None:
        raise NotImplementedError


class InMemoryEventPublisher(IEventPublisher):
    def __init__(self) -> None:
        self.events: list[PublishedEvent] = []

    async def publish_async(self, event_type: str, data: dict[str, Any]) -> None:
        self.events.append(
            PublishedEvent(
                event_type=event_type,
                data=dict(data),
                published_at=datetime.now(timezone.utc).isoformat(),
            )
        )
