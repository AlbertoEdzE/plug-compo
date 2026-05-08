import pytest

from options import IntelligentPrefillOptions
from providers.mock_prefill import MockPrefillAdapter
from tests.synthesizers.prefill_synthesizer import PrefillSynthesizer


pytestmark = pytest.mark.asyncio


async def test_mock_returns_mock_value_and_confidence_for_all_fields() -> None:
    synth = PrefillSynthesizer(seed=1)
    fields = synth.unmapped_fields(3)
    req = synth.request(unmapped_fields=fields)

    adapter = MockPrefillAdapter(IntelligentPrefillOptions(prompt_version="v1"))
    result = await adapter.prefill_async(req)

    assert result.total_fields_requested == 3
    assert all(r.value == "MOCK_VALUE" for r in result.field_results)
    assert all(r.confidence == 0.80 for r in result.field_results)
    assert all(r.needs_review is False for r in result.field_results)
