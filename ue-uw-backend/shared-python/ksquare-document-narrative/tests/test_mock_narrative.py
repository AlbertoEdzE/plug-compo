import pytest

from contracts import NarrativeType
from options import DocumentNarrativeOptions
from providers.mock_narrative import MockNarrativeAdapter
from tests.synthesizers.narrative_synthesizer import NarrativeSynthesizer


pytestmark = pytest.mark.asyncio


async def test_mock_risk_summary_contains_institution_name() -> None:
    synth = NarrativeSynthesizer(seed=1)
    req = synth.request(narrative_type=NarrativeType.RISK_SUMMARY)

    adapter = MockNarrativeAdapter(DocumentNarrativeOptions(prompt_version="v1"))
    result = await adapter.generate_narrative_async(req)

    assert req.submission_context.institution_name in result.narrative_text


async def test_mock_referral_memo_returns_sections_dict_with_multiple_keys() -> None:
    synth = NarrativeSynthesizer(seed=2)
    req = synth.request(narrative_type=NarrativeType.REFERRAL_MEMO)

    adapter = MockNarrativeAdapter(DocumentNarrativeOptions(prompt_version="v1"))
    result = await adapter.generate_narrative_async(req)

    assert len(result.sections.keys()) >= 4
