from contracts import IntelligentPrefillAdapter
from options import IntelligentPrefillOptions
from providers.azure_openai_prefill import AzureOpenAiPrefillAdapter
from providers.mock_prefill import MockPrefillAdapter


def resolve_adapter(options: IntelligentPrefillOptions) -> IntelligentPrefillAdapter:
    provider = (options.provider or "AzureOpenAi").strip()
    if provider.lower() == "mock":
        return MockPrefillAdapter(options)

    return AzureOpenAiPrefillAdapter(options)
