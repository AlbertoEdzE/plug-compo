import json
import os
from dataclasses import asdict

import azure.functions as func

from contracts import PrefillRequest, UnmappedField
from factory import resolve_adapter
from options import IntelligentPrefillOptions

app = func.FunctionApp()


def _load_options() -> IntelligentPrefillOptions:
    return IntelligentPrefillOptions(
        provider=os.getenv("INTELLIGENT_PREFILL_PROVIDER", "AzureOpenAi"),
        azure_openai_endpoint=os.getenv("AZURE_OPENAI_ENDPOINT", ""),
        deployment_name=os.getenv("AZURE_OPENAI_DEPLOYMENT", "gpt-4o"),
        prompt_version=os.getenv("INTELLIGENT_PREFILL_PROMPT_VERSION", "v1"),
        max_document_chars=int(os.getenv("INTELLIGENT_PREFILL_MAX_DOCUMENT_CHARS", "8000")),
        review_confidence_threshold=float(os.getenv("INTELLIGENT_PREFILL_REVIEW_THRESHOLD", "0.75")),
        fields_per_batch=int(os.getenv("INTELLIGENT_PREFILL_FIELDS_PER_BATCH", "15")),
    )


@app.function_name("IntelligentPrefill")
@app.route(route="prefill/run", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def intelligent_prefill(req: func.HttpRequest) -> func.HttpResponse:
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON.", status_code=400)

    try:
        request = PrefillRequest(
            document_id=body["document_id"],
            document_text=body["document_text"],
            document_type=body["document_type"],
            unmapped_fields=[UnmappedField(**f) for f in body.get("unmapped_fields", [])],
            correlation_id=body.get("correlation_id"),
        )
    except (KeyError, TypeError) as ex:
        return func.HttpResponse(f"Invalid request shape: {ex}", status_code=400)

    options = _load_options()
    adapter = resolve_adapter(options)
    result = await adapter.prefill_async(request)
    return func.HttpResponse(json.dumps(asdict(result)), mimetype="application/json")
