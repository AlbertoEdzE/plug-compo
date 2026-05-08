from dataclasses import dataclass


@dataclass
class AiEmailTriageOptions:
    provider: str = "AzureOpenAi"
    azure_openai_endpoint: str = ""
    deployment_name: str = "gpt-4o-mini"
    prompt_version: str = "v1"
    max_body_chars: int = 2000
    temperature: float = 0.0
    api_version: str = "2025-01-01-preview"

