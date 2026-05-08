from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Optional


@dataclass
class UnmappedField:
    canonical_field: str
    display_label: str
    expected_type: str
    description: str


@dataclass
class PrefillFieldResult:
    canonical_field: str
    value: Optional[str]
    confidence: float
    source_text: str
    reasoning: str
    needs_review: bool


@dataclass
class PrefillRequest:
    document_id: str
    document_text: str
    document_type: str
    unmapped_fields: list[UnmappedField]
    correlation_id: Optional[str] = None


@dataclass
class PrefillResult:
    document_id: str
    field_results: list[PrefillFieldResult]
    total_fields_requested: int
    total_fields_filled: int
    total_needs_review: int
    model_version: str
    prompt_version: str
    latency_ms: int
    correlation_id: Optional[str] = None

    @property
    def fill_rate(self) -> float:
        return self.total_fields_filled / max(self.total_fields_requested, 1)


class IntelligentPrefillAdapter(ABC):
    @abstractmethod
    async def prefill_async(self, request: PrefillRequest) -> PrefillResult:
        raise NotImplementedError
