from contracts import DocumentNarrativeAdapter
from options import DocumentNarrativeOptions
from providers.azure_openai_narrative import AzureOpenAiNarrativeAdapter
from providers.mock_narrative import MockNarrativeAdapter


def resolve_adapter(options: DocumentNarrativeOptions) -> DocumentNarrativeAdapter:
    provider = (options.provider or "AzureOpenAi").strip()
    if provider.lower() == "mock":
        return MockNarrativeAdapter(options)

    return AzureOpenAiNarrativeAdapter(options)
