from __future__ import annotations

from dataclasses import dataclass
from typing import Optional


@dataclass
class LlmObservabilityConfig:
    app_insights_connection_string: str = ""
    enable_azure_monitor: bool = True

    langsmith_api_key: Optional[str] = None
    langsmith_project: str = "ue-uw-ag-ui"
    enable_langsmith: bool = False

    enable_offline_evaluation: bool = True
    evaluation_schedule_cron: str = "0 2 * * *"
    evaluation_dataset_min_size: int = 50
    ragas_judge_model: str = "gpt-4o-mini"
    regression_threshold: float = 0.05

    daily_cost_alert_usd: float = 200.0
    monthly_cost_budget_usd: float = 2000.0

    connection_string: str = ""

