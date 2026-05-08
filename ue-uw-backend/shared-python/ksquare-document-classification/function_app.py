import json
import os

import azure.functions as func

from ksquare.document_classification.config import ClassificationConfig
from ksquare.document_classification.models import DocumentInput
from ksquare.document_classification.pipeline import AzureThenHeuristicPipeline
from ksquare.document_classification.providers.heuristic_classifier import HeuristicDocumentClassifier
from ksquare.document_classification.providers.mock_classifier import MockDocumentClassifier

app = func.FunctionApp(http_auth_level=func.AuthLevel.FUNCTION)


def _create_classifier():
    provider = os.getenv("KSQUARE_DOC_CLASSIFY_PROVIDER", "azure_then_heuristic").strip().lower()
    if provider == "mock":
        return MockDocumentClassifier()

    if provider == "heuristic_only":
        return HeuristicDocumentClassifier()

    azure_endpoint = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT", "")
    model_id = os.getenv("AZURE_DOCUMENT_CLASSIFIER_MODEL_ID", "ksquare-doc-classifier-v1")
    use_mi = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_USE_MANAGED_IDENTITY", "true").strip().lower() == "true"
    api_key = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_API_KEY")

    config = ClassificationConfig(
        provider=provider,
        azure_endpoint=azure_endpoint,
        azure_classifier_model_id=model_id,
        use_managed_identity=use_mi,
        azure_api_key=api_key,
    )

    return AzureThenHeuristicPipeline(config)


@app.route(route="classify", methods=["POST"])
async def classify(req: func.HttpRequest) -> func.HttpResponse:
    try:
        body = req.get_json()
        correlation_id = body.get("correlationId")

        document = DocumentInput.model_validate(
            {
                "blob_path": body.get("blobPath"),
                "document_uri": body.get("documentUri"),
                "content_base64": body.get("contentBase64"),
                "content_type": body.get("contentType"),
                "file_name": body.get("fileName"),
                "first_page_text": body.get("firstPageText"),
            }
        )

        classifier = _create_classifier()
        result = await classifier.classify_async(document, correlation_id=correlation_id)
        return func.HttpResponse(json.dumps(result.model_dump()), mimetype="application/json", status_code=200)
    except Exception as ex:
        return func.HttpResponse(
            json.dumps({"error": str(ex)}),
            mimetype="application/json",
            status_code=400,
        )
