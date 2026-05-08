import json

import pytest
import respx
from httpx import Response

from contracts import UnmappedField
from options import IntelligentPrefillOptions
from providers.azure_openai_prefill import AzureOpenAiPrefillAdapter
from tests.synthesizers.prefill_synthesizer import PrefillSynthesizer


pytestmark = pytest.mark.asyncio


def _endpoint_url(base: str, deployment: str) -> str:
    return f"{base.rstrip('/')}/openai/deployments/{deployment}/chat/completions"


@respx.mock
async def test_azure_openai_parses_results_and_sets_needs_review() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o"

    route = respx.post(_endpoint_url(base, deployment)).mock(
        return_value=Response(
            200,
            json={
                "id": "chatcmpl-1",
                "model": deployment,
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": json.dumps(
                                {
                                    "results": [
                                        {
                                            "canonical_field": "total_enrollment",
                                            "value": "4250",
                                            "confidence": 0.82,
                                            "source_text": "Total student enrollment: 4,250",
                                            "reasoning": "Found labeled field with clear numeric value.",
                                        }
                                    ]
                                }
                            ),
                        },
                    }
                ],
                "usage": {"prompt_tokens": 10, "completion_tokens": 20, "total_tokens": 30},
            },
        )
    )

    options = IntelligentPrefillOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        max_document_chars=8000,
        review_confidence_threshold=0.75,
        fields_per_batch=15,
    )
    adapter = AzureOpenAiPrefillAdapter(options, azure_ad_token_provider=lambda: "test-token")

    synth = PrefillSynthesizer(seed=10)
    fields = [
        UnmappedField(
            canonical_field="total_enrollment",
            display_label="Total Enrollment",
            expected_type="integer",
            description="Total number of enrolled students across all grades",
        )
    ]
    req = synth.request(unmapped_fields=fields, document_text="Total student enrollment: 4,250")
    result = await adapter.prefill_async(req)

    assert result.total_fields_requested == 1
    assert result.total_fields_filled == 1
    assert result.field_results[0].canonical_field == "total_enrollment"
    assert result.field_results[0].needs_review is False
    assert route.called


@respx.mock
async def test_azure_openai_empty_unmapped_fields_returns_immediately_no_llm_call() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o"

    route = respx.post(_endpoint_url(base, deployment)).mock(return_value=Response(200, json={}))

    options = IntelligentPrefillOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
    )
    adapter = AzureOpenAiPrefillAdapter(options, azure_ad_token_provider=lambda: "test-token")

    req = PrefillSynthesizer(seed=11).request(unmapped_fields=[])
    result = await adapter.prefill_async(req)

    assert result.total_fields_requested == 0
    assert result.field_results == []
    assert route.called is False


@respx.mock
async def test_azure_openai_malformed_json_retries_once_then_returns_all_null_fallback() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o"

    route = respx.post(_endpoint_url(base, deployment))
    route.side_effect = [
        Response(
            200,
            json={
                "id": "chatcmpl-1",
                "model": deployment,
                "choices": [{"index": 0, "message": {"role": "assistant", "content": "{not-json"}}],
            },
        ),
        Response(
            200,
            json={
                "id": "chatcmpl-2",
                "model": deployment,
                "choices": [{"index": 0, "message": {"role": "assistant", "content": "{still-not-json"}}],
            },
        ),
    ]

    options = IntelligentPrefillOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        review_confidence_threshold=0.75,
        fields_per_batch=15,
    )
    adapter = AzureOpenAiPrefillAdapter(options, azure_ad_token_provider=lambda: "test-token")

    synth = PrefillSynthesizer(seed=12)
    fields = synth.unmapped_fields(2)
    req = synth.request(unmapped_fields=fields, document_text="Some doc text.")
    result = await adapter.prefill_async(req)

    assert len(result.field_results) == 2
    assert all(r.value is None for r in result.field_results)
    assert all(r.confidence == 0.0 for r in result.field_results)
    assert all(r.needs_review is True for r in result.field_results)
    assert route.call_count == 2


@respx.mock
async def test_azure_openai_batches_20_fields_into_two_llm_calls() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o"

    synth = PrefillSynthesizer(seed=13)
    fields = synth.unmapped_fields(20)

    def _response_for(batch_fields) -> Response:
        return Response(
            200,
            json={
                "id": "chatcmpl-x",
                "model": deployment,
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": json.dumps(
                                {
                                    "results": [
                                        {
                                            "canonical_field": f.canonical_field,
                                            "value": "X",
                                            "confidence": 0.80,
                                            "source_text": "X",
                                            "reasoning": "Mocked LLM response.",
                                        }
                                        for f in batch_fields
                                    ]
                                }
                            ),
                        },
                    }
                ],
            },
        )

    route = respx.post(_endpoint_url(base, deployment))
    route.side_effect = [_response_for(fields[:15]), _response_for(fields[15:])]

    options = IntelligentPrefillOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        review_confidence_threshold=0.75,
        fields_per_batch=15,
    )
    adapter = AzureOpenAiPrefillAdapter(options, azure_ad_token_provider=lambda: "test-token")

    req = synth.request(unmapped_fields=fields, document_text="Some document text.")
    result = await adapter.prefill_async(req)

    assert result.total_fields_requested == 20
    assert len(result.field_results) == 20
    assert route.call_count == 2
