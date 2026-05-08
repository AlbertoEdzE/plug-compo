import pytest

from providers.mock_triage import MockEmailTriageAdapter
from tests.synthesizers.email_triage_synthesizer import EmailTriageSynthesizer


pytestmark = pytest.mark.asyncio


async def test_mock_returns_renewal_intent_when_keyword_present() -> None:
    synth = EmailTriageSynthesizer(seed=1)
    req = synth.request(body_text="This is a renewal request for our expiring policy.")

    adapter = MockEmailTriageAdapter()
    result = await adapter.triage_async(req)

    assert result.intent == "Renewal"


async def test_mock_returns_k12_queue_when_school_district_present() -> None:
    synth = EmailTriageSynthesizer(seed=2)
    req = synth.request(body_text="Please quote this school district for GL and Property.")

    adapter = MockEmailTriageAdapter()
    result = await adapter.triage_async(req)

    assert result.routing_suggestion == "K12-UW-Queue"


async def test_mock_returns_urgent_when_two_or_more_urgency_keywords_present() -> None:
    synth = EmailTriageSynthesizer(seed=3)
    req = synth.request(body_text="This is urgent and expiring tomorrow. Need a response asap.")

    adapter = MockEmailTriageAdapter()
    result = await adapter.triage_async(req)

    assert result.urgency == "Urgent"

