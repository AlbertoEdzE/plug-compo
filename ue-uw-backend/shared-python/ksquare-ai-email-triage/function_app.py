import json
import os
from dataclasses import asdict

import azure.functions as func

from contracts import EmailTriageRequest
from factory import resolve_adapter
from options import AiEmailTriageOptions

app = func.FunctionApp()


def _load_options() -> AiEmailTriageOptions:
    return AiEmailTriageOptions(
        provider=os.getenv("AI_EMAIL_TRIAGE_PROVIDER", "AzureOpenAi"),
        azure_openai_endpoint=os.getenv("AZURE_OPENAI_ENDPOINT", ""),
        deployment_name=os.getenv("AZURE_OPENAI_DEPLOYMENT", "gpt-4o-mini"),
        prompt_version=os.getenv("AI_EMAIL_TRIAGE_PROMPT_VERSION", "v1"),
        max_body_chars=int(os.getenv("AI_EMAIL_TRIAGE_MAX_BODY_CHARS", "2000")),
        temperature=float(os.getenv("AI_EMAIL_TRIAGE_TEMPERATURE", "0.0")),
    )


@app.function_name("EmailTriage")
@app.route(route="email/triage", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def email_triage(req: func.HttpRequest) -> func.HttpResponse:
    try:
        body = req.get_json()
    except ValueError:
        return func.HttpResponse("Invalid JSON.", status_code=400)

    try:
        request = EmailTriageRequest(**body)
    except TypeError as ex:
        return func.HttpResponse(f"Invalid request shape: {ex}", status_code=400)

    options = _load_options()
    adapter = resolve_adapter(options)
    result = await adapter.triage_async(request)
    return func.HttpResponse(json.dumps(asdict(result)), mimetype="application/json")
