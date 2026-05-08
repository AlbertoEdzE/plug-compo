import pytest
import respx
from httpx import Response

from contracts import NarrativeType
from options import DocumentNarrativeOptions
from providers.azure_openai_narrative import AzureOpenAiNarrativeAdapter, LlmTracer
from tests.synthesizers.narrative_synthesizer import NarrativeSynthesizer


pytestmark = pytest.mark.asyncio


def _endpoint_url(base: str, deployment: str) -> str:
    return f"{base.rstrip('/')}/openai/deployments/{deployment}/chat/completions"


@respx.mock
async def test_azure_openai_risk_summary_returns_text_and_traces_operation() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o"

    route = respx.post(_endpoint_url(base, deployment)).mock(
        return_value=Response(
            200,
            json={
                "id": "chatcmpl-1",
                "model": deployment,
                "choices": [{"index": 0, "message": {"role": "assistant", "content": "A concise risk summary."}}],
                "usage": {"prompt_tokens": 11, "completion_tokens": 22, "total_tokens": 33},
            },
        )
    )

    tracer = LlmTracer()
    options = DocumentNarrativeOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        temperature=0.3,
    )
    adapter = AzureOpenAiNarrativeAdapter(options, azure_ad_token_provider=lambda: "test-token", tracer=tracer)

    req = NarrativeSynthesizer(seed=10).request(narrative_type=NarrativeType.RISK_SUMMARY, with_loss_history=False)
    result = await adapter.generate_narrative_async(req)

    assert result.narrative_text != ""
    assert tracer.records[-1].operation == "narrative_risksummary"
    assert route.called


@respx.mock
async def test_azure_openai_referral_memo_parses_sections() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o"

    memo = (
        "1. SUBMISSION OVERVIEW:\n"
        "Overview text.\n"
        "2. KEY RISK FACTORS:\n"
        "- Factor A\n"
        "- Factor B\n"
        "3. LOSS HISTORY SUMMARY:\n"
        "Loss text.\n"
        "4. APPETITE ASSESSMENT:\n"
        "Appetite text.\n"
        "5. REFERRAL REASON:\n"
        "Reason text.\n"
        "6. RECOMMENDED ACTION:\n"
        "Approve\n"
    )

    respx.post(_endpoint_url(base, deployment)).mock(
        return_value=Response(
            200,
            json={
                "id": "chatcmpl-2",
                "model": deployment,
                "choices": [{"index": 0, "message": {"role": "assistant", "content": memo}}],
                "usage": {"prompt_tokens": 10, "completion_tokens": 20, "total_tokens": 30},
            },
        )
    )

    options = DocumentNarrativeOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        temperature=0.3,
    )
    adapter = AzureOpenAiNarrativeAdapter(options, azure_ad_token_provider=lambda: "test-token")

    req = NarrativeSynthesizer(seed=11).request(narrative_type=NarrativeType.REFERRAL_MEMO)
    result = await adapter.generate_narrative_async(req)

    assert len(result.sections.keys()) >= 4


@respx.mock
async def test_azure_openai_api_error_returns_empty_narrative_text_no_exception() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o"

    respx.post(_endpoint_url(base, deployment)).mock(return_value=Response(500, json={"error": "down"}))

    options = DocumentNarrativeOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        temperature=0.3,
    )
    adapter = AzureOpenAiNarrativeAdapter(options, azure_ad_token_provider=lambda: "test-token")

    req = NarrativeSynthesizer(seed=12).request(narrative_type=NarrativeType.RISK_SUMMARY, with_loss_history=False)
    result = await adapter.generate_narrative_async(req)

    assert result.narrative_text == ""
