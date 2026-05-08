from ksquare.document_extraction.config import ExtractionConfig
from ksquare.document_extraction.models import ExtractedField, ExtractedTable
from ksquare.document_extraction.providers.azure_extractor import AzureDocumentExtractor


def test_low_confidence_field_sets_pending_review():
    extractor = AzureDocumentExtractor(
        ExtractionConfig(
            endpoint="https://example.com",
            use_managed_identity=True,
            low_confidence_threshold=0.75,
        )
    )

    status = extractor._compute_status(
        fields=[
            ExtractedField(name="a", value="b", confidence=0.95),
            ExtractedField(name="c", value="d", confidence=0.5),
        ],
        tables=[],
    )

    assert status.value == "PendingReview"


def test_no_fields_and_no_tables_sets_failed():
    extractor = AzureDocumentExtractor(ExtractionConfig(endpoint="https://example.com", use_managed_identity=True))
    status = extractor._compute_status(fields=[], tables=[])
    assert status.value == "Failed"
