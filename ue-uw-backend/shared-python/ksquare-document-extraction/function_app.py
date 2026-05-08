import json
import os

import azure.functions as func

from ksquare.document_extraction.config import ExtractionConfig
from ksquare.document_extraction.models import DocumentInput
from ksquare.document_extraction.providers.azure_extractor import AzureDocumentExtractor
from ksquare.document_extraction.providers.mock_extractor import MockDocumentExtractor

app = func.FunctionApp(http_auth_level=func.AuthLevel.FUNCTION)


def _create_extractor():
    provider = os.getenv("KSQUARE_DOC_EXTRACT_PROVIDER", "azure").strip().lower()
    if provider == "mock":
        return MockDocumentExtractor()

    endpoint = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT")
    if not endpoint:
        raise ValueError("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT must be set.")

    use_mi = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_USE_MANAGED_IDENTITY", "true").strip().lower() == "true"
    api_key = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_API_KEY")

    return AzureDocumentExtractor(
        ExtractionConfig(endpoint=endpoint, use_managed_identity=use_mi, api_key=api_key)
    )


@app.route(route="extract", methods=["POST"])
async def extract(req: func.HttpRequest) -> func.HttpResponse:
    try:
        body = req.get_json()
        model_hint = body.get("modelHint")
        correlation_id = body.get("correlationId")

        document = DocumentInput.model_validate(
            {
                "blob_path": body.get("blobPath"),
                "document_uri": body.get("documentUri"),
                "content_base64": body.get("contentBase64"),
                "content_type": body.get("contentType"),
                "file_name": body.get("fileName"),
            }
        )

        extractor = _create_extractor()
        result = await extractor.extract_async(document, model_hint=model_hint, correlation_id=correlation_id)
        payload = result.model_dump()

        return func.HttpResponse(json.dumps(payload), mimetype="application/json", status_code=200)
    except Exception as ex:
        return func.HttpResponse(
            json.dumps({"error": str(ex)}),
            mimetype="application/json",
            status_code=400,
        )
