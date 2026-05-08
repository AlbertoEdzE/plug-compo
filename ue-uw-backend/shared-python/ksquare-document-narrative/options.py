from dataclasses import dataclass


@dataclass
class DocumentNarrativeOptions:
    provider: str = "AzureOpenAi"
    azure_openai_endpoint: str = ""
    deployment_name: str = "gpt-4o"
    prompt_version: str = "v1"
    temperature: float = 0.3
    api_version: str = "2025-01-01-preview"
