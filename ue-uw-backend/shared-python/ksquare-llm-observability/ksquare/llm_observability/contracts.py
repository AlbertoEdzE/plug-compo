from __future__ import annotations

from abc import ABC, abstractmethod
from datetime import date
from typing import Optional

from .models import CostSummary, EvaluationDataset, EvaluationRunResult, LlmMetricsBatch


class IEvaluationPipeline(ABC):
    @abstractmethod
    async def run_offline_evaluation_async(
        self,
        dataset: EvaluationDataset,
        run_name: Optional[str] = None,
    ) -> EvaluationRunResult:
        ...


class ICostTracker(ABC):
    @abstractmethod
    async def get_daily_cost_async(self, day: date) -> CostSummary:
        ...

    @abstractmethod
    async def get_period_cost_async(self, from_date: date, to_date: date) -> CostSummary:
        ...


class IObservabilityExporter(ABC):
    @abstractmethod
    def export_to_langsmith(self, trace: dict) -> None:
        ...

    @abstractmethod
    def export_to_app_insights(self, metrics: LlmMetricsBatch) -> None:
        ...

