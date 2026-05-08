import json
import os
from dataclasses import asdict

import azure.functions as func

from contracts import LossHistoryContext, NarrativeRequest, NarrativeType, SubmissionContext
from factory import resolve_adapter
from options import DocumentNarrativeOptions

app = func.FunctionApp()


def _load_options() -> DocumentNarrativeOptions:
    return DocumentNarrativeOptions(
        provider=os.getenv("DOCUMENT_NARRATIVE_PROVIDER", "AzureOpenAi"),
        azure_openai_endpoint=os.getenv("AZURE_OPENAI_ENDPOINT", ""),
        deployment_name=os.getenv("AZURE_OPENAI_DEPLOYMENT", "gpt-4o"),
        prompt_version=os.getenv("DOCUMENT_NARRATIVE_PROMPT_VERSION", "v1"),
        temperature=float(os.getenv("DOCUMENT_NARRATIVE_TEMPERATURE", "0.3")),
    )


@app.function_name("GenerateNarrative")
@app.route(route="narrative/generate", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def generate_narrative(req: func.HttpRequest) -> func.HttpResponse:
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON.", status_code=400)

    try:
        request = NarrativeRequest(
            submission_id=body["submission_id"],
            narrative_type=NarrativeType(body["narrative_type"]),
            submission_context=SubmissionContext(**body["submission_context"]),
            loss_history=LossHistoryContext(**body["loss_history"]) if body.get("loss_history") else None,
            underwriter_name=body.get("underwriter_name"),
            additional_notes=body.get("additional_notes"),
            correlation_id=body.get("correlation_id"),
        )
    except (KeyError, TypeError, ValueError) as ex:
        return func.HttpResponse(f"Invalid request shape: {ex}", status_code=400)

    options = _load_options()
    adapter = resolve_adapter(options)
    result = await adapter.generate_narrative_async(request)
    return func.HttpResponse(json.dumps(asdict(result)), mimetype="application/json")
