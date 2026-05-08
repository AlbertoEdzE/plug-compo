from __future__ import annotations

from dataclasses import dataclass
from typing import Optional


@dataclass
class AgentOrchestratorConfig:
    azure_openai_endpoint: str = ""
    azure_openai_deployment: str = "gpt-4.1"
    azure_openai_api_version: str = "2024-12-01-preview"
    use_managed_identity: bool = True
    api_key: Optional[str] = None

    max_context_tokens: int = 100_000
    system_prompt_reserved_tokens: int = 5_000
    temperature: float = 0.3
    max_completion_tokens: int = 2048

    content_safety_endpoint: str = ""
    content_safety_api_key: str = ""
    enable_safety_check: bool = True

    azure_search_endpoint: str = ""
    search_index_name: str = "submission-docs"
    rag_top_k: int = 5

    application_insights_connection_string: str = ""
    langsmith_api_key: Optional[str] = None
    enable_online_evaluation: bool = True

    prompt_version: str = "v1"
    ab_test_enabled: bool = False

    requests_per_minute_per_user: int = 10
    requests_per_hour_per_user: int = 50

