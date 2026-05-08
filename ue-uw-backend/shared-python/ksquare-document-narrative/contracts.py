from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from enum import Enum
from typing import Optional


class NarrativeType(str, Enum):
    RISK_SUMMARY = "RiskSummary"
    LOSS_RUN_NARRATIVE = "LossRunNarrative"
    REFERRAL_MEMO = "ReferralMemo"
    UNDERWRITER_FILE_NOTE = "UnderwriterFileNote"


@dataclass
class SubmissionContext:
    submission_id: str
    institution_name: str
    institution_type: str
    state: str
    naics_code: str
    total_insured_value: float
    enrollment: int
    fte_employees: int
    effective_date: str
    expiration_date: str
    coverage_lines: list[dict]
    risk_indicators: dict
    appetite_fit_score: float
    appetite_classification: str


@dataclass
class LossHistoryContext:
    five_year_avg_loss_ratio: float
    largest_single_loss: float
    total_claims_count: int
    loss_trend: str
    loss_run_years: list[dict]


@dataclass
class NarrativeRequest:
    submission_id: str
    narrative_type: NarrativeType
    submission_context: SubmissionContext
    loss_history: Optional[LossHistoryContext] = None
    underwriter_name: Optional[str] = None
    additional_notes: Optional[str] = None
    correlation_id: Optional[str] = None


@dataclass
class NarrativeResult:
    submission_id: str
    narrative_type: NarrativeType
    narrative_text: str
    sections: dict[str, str]
    word_count: int
    model_version: str
    prompt_version: str
    latency_ms: int
    correlation_id: Optional[str] = None


class DocumentNarrativeAdapter(ABC):
    @abstractmethod
    async def generate_narrative_async(self, request: NarrativeRequest) -> NarrativeResult:
        raise NotImplementedError
