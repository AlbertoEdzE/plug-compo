import pytest

from ksquare.document_classification.models import DocumentInput
from ksquare.document_classification.providers.heuristic_classifier import HeuristicDocumentClassifier


@pytest.mark.asyncio
async def test_heuristic_classifier_returns_acord125_for_keywords():
    classifier = HeuristicDocumentClassifier()
    result = await classifier.classify_async(
        DocumentInput(
            blob_path="https://example.com/doc.pdf",
            content_type="application/pdf",
            file_name="submission_acord125.pdf",
            first_page_text="This is an ACORD 125 commercial lines application",
        )
    )

    assert result.document_type == "ACORD125"
    assert result.confidence > 0.0


@pytest.mark.asyncio
async def test_heuristic_classifier_returns_unknown_when_no_keywords():
    classifier = HeuristicDocumentClassifier()
    result = await classifier.classify_async(
        DocumentInput(
            blob_path="https://example.com/doc.pdf",
            content_type="application/pdf",
            file_name="random.pdf",
            first_page_text="lorem ipsum",
        )
    )

    assert result.document_type == "Unknown"
