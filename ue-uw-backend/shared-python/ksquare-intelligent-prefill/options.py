from dataclasses import dataclass


@dataclass
class IntelligentPrefillOptions:
    provider: str = "AzureOpenAi"
    azure_openai_endpoint: str = ""
    deployment_name: str = "gpt-4o"
    prompt_version: str = "v1"
    max_document_chars: int = 8000
    review_confidence_threshold: float = 0.75
    fields_per_batch: int = 15
