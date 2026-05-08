import pytest

from ksquare.document_classification.config import ClassificationConfig
from ksquare.document_classification.models import ClassificationMethod, ClassificationResult, DocumentInput
from ksquare.document_classification.pipeline import AzureThenHeuristicPipeline


@pytest.mark.asyncio
async def test_pipeline_uses_azure_when_confidence_above_threshold(mocker):
    cfg = ClassificationConfig(
        azure_endpoint="https://example.com",
        azure_classifier_model_id="model",
        confidence_threshold_auto=0.85,
    )
    pipeline = AzureThenHeuristicPipeline(cfg)

    mocker.patch.object(
        pipeline._azure,
        "classify_async",
        return_value=ClassificationResult(
            document_type="LossRun",
            confidence=0.9,
            method=ClassificationMethod.AZURE_DOCUMENT_CLASSIFIER,
        ),
    )

    result = await pipeline.classify_async(DocumentInput(blob_path="x", content_type="application/pdf"))
    assert result.document_type == "LossRun"
    assert result.method == ClassificationMethod.AZURE_DOCUMENT_CLASSIFIER


@pytest.mark.asyncio
async def test_pipeline_falls_back_to_heuristic_when_azure_below_threshold(mocker):
    cfg = ClassificationConfig(
        azure_endpoint="https://example.com",
        azure_classifier_model_id="model",
        confidence_threshold_auto=0.85,
    )
    pipeline = AzureThenHeuristicPipeline(cfg)

    mocker.patch.object(
        pipeline._azure,
        "classify_async",
        return_value=ClassificationResult(
            document_type="Certificate",
            confidence=0.4,
            method=ClassificationMethod.AZURE_DOCUMENT_CLASSIFIER,
        ),
    )

    result = await pipeline.classify_async(
        DocumentInput(
            blob_path="x",
            content_type="application/pdf",
            file_name="acord125.pdf",
            first_page_text="acord 125",
        )
    )
    assert result.document_type == "ACORD125"
    assert result.method == ClassificationMethod.HEURISTIC_KEYWORD


def test_requires_manual_review_is_true_when_confidence_below_review_threshold():
    r = ClassificationResult(
        document_type="ACORD125",
        confidence=0.69,
        method=ClassificationMethod.AZURE_DOCUMENT_CLASSIFIER,
    )
    assert r.requires_manual_review is True
