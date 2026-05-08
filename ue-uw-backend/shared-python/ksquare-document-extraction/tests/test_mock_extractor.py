import os
from pathlib import Path

import pytest

from ksquare.document_extraction.models import DocumentInput
from ksquare.document_extraction.providers.mock_extractor import MockDocumentExtractor


@pytest.mark.asyncio
async def test_mock_extractor_loads_fixture_and_overrides_correlation_id(tmp_path: Path):
    fixture_src = Path(__file__).parent / "fixtures" / "sample_acord125.json"
    fixture_dest = tmp_path / "fixture.json"
    fixture_dest.write_text(fixture_src.read_text(encoding="utf-8"), encoding="utf-8")

    os.environ["KSQUARE_DOC_EXTRACT_FIXTURE_PATH"] = str(fixture_dest)

    extractor = MockDocumentExtractor()
    result = await extractor.extract_async(
        DocumentInput(blob_path="https://example.com/doc.pdf", content_type="application/pdf"),
        model_hint="ACORD125",
        correlation_id="corr-123",
    )

    assert result.document_id == "corr-123"
    assert result.correlation_id == "corr-123"
    assert len(result.fields) >= 1
