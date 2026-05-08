from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Optional


@dataclass
class EmailTriageRequest:
    email_id: str
    subject: str
    body_text: str
    sender_email: str
    sender_name: Optional[str]
    received_at: str
    attachment_names: list[str] = field(default_factory=list)
    correlation_id: Optional[str] = None


@dataclass
class ExtractedEmailEntity:
    field_name: str
    value: str
    confidence: float
    source_text: str


@dataclass
class EmailTriageResult:
    email_id: str
    intent: str
    intent_confidence: float
    extracted_entities: list[ExtractedEmailEntity]
    routing_suggestion: str
    urgency: str
    urgency_signals: list[str]
    summary: str
    model_version: str
    prompt_version: str
    latency_ms: int
    correlation_id: Optional[str]


class AiEmailTriageAdapter(ABC):
    @abstractmethod
    async def triage_async(self, request: EmailTriageRequest) -> EmailTriageResult:
        raise NotImplementedError

