import json

import pytest
import respx
from httpx import Response

from options import AiEmailTriageOptions
from providers.azure_openai_triage import AzureOpenAiEmailTriageAdapter
from tests.synthesizers.email_triage_synthesizer import EmailTriageSynthesizer


pytestmark = pytest.mark.asyncio


def _endpoint_url(base: str, deployment: str) -> str:
    return f"{base.rstrip('/')}/openai/deployments/{deployment}/chat/completions"


@respx.mock
async def test_azure_openai_parses_json_response_into_result() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o-mini"

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
                                    "intent": "NewSubmission",
                                    "intent_confidence": 0.91,
                                    "routing_suggestion": "K12-UW-Queue",
                                    "urgency": "Normal",
                                    "urgency_signals": [],
                                    "summary": "Broker submitted a new account for quoting.",
                                    "entities": [
                                        {
                                            "field_name": "institution_name",
                                            "value": "Acme School District",
                                            "confidence": 0.88,
                                            "source_text": "Acme School District",
                                        }
                                    ],
                                }
                            ),
                        },
                    }
                ],
                "usage": {"prompt_tokens": 10, "completion_tokens": 20, "total_tokens": 30},
            },
        )
    )

    options = AiEmailTriageOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        max_body_chars=2000,
        temperature=0.0,
    )
    adapter = AzureOpenAiEmailTriageAdapter(options, azure_ad_token_provider=lambda: "test-token")

    req = EmailTriageSynthesizer(seed=10).request(body_text="Please quote a new school district account.")
    result = await adapter.triage_async(req)

    assert result.intent == "NewSubmission"
    assert result.routing_suggestion == "K12-UW-Queue"
    assert result.extracted_entities[0].field_name == "institution_name"
    assert route.called


@respx.mock
async def test_azure_openai_malformed_json_retries_once_then_returns_safe_default() -> None:
    base = "https://example.openai.azure.com"
    deployment = "gpt-4o-mini"

    route = respx.post(_endpoint_url(base, deployment))
    route.side_effect = [
        Response(
            200,
            json={
                "id": "chatcmpl-1",
                "model": deployment,
                "choices": [{"index": 0, "message": {"role": "assistant", "content": "{not-json"}}],
                "usage": {"prompt_tokens": 10, "completion_tokens": 20, "total_tokens": 30},
            },
        ),
        Response(
            200,
            json={
                "id": "chatcmpl-2",
                "model": deployment,
                "choices": [{"index": 0, "message": {"role": "assistant", "content": "{still-not-json"}}],
                "usage": {"prompt_tokens": 10, "completion_tokens": 20, "total_tokens": 30},
            },
        ),
    ]

    options = AiEmailTriageOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=base,
        deployment_name=deployment,
        prompt_version="v1",
        max_body_chars=2000,
        temperature=0.0,
    )
    adapter = AzureOpenAiEmailTriageAdapter(options, azure_ad_token_provider=lambda: "test-token")

    req = EmailTriageSynthesizer(seed=11).request(body_text="Hello, please help.")
    result = await adapter.triage_async(req)

    assert result.intent == "Other"
    assert result.routing_suggestion == "Manual"
    assert route.call_count == 2

