from contracts import AiEmailTriageAdapter
from options import AiEmailTriageOptions
from providers.azure_openai_triage import AzureOpenAiEmailTriageAdapter
from providers.mock_triage import MockEmailTriageAdapter


def resolve_adapter(options: AiEmailTriageOptions) -> AiEmailTriageAdapter:
    provider = (options.provider or "AzureOpenAi").strip()
    if provider.lower() == "mock":
        return MockEmailTriageAdapter()

    return AzureOpenAiEmailTriageAdapter(options)

