import json
import os
import uuid
from dataclasses import asdict

import azure.functions as func

from ksquare.agent_orchestrator import AgentOrchestrator
from ksquare.agent_orchestrator.config import AgentOrchestratorConfig
from ksquare.agent_orchestrator.models import AgentChatRequest, ChatMessage, UserFeedback

app = func.FunctionApp()
orchestrator = AgentOrchestrator(AgentOrchestratorConfig(), audit_sqlite_path=os.getenv("KSQUARE_AGENT_AUDIT_SQLITE_PATH", "agent_audit.sqlite3"))


def _config_from_env() -> AgentOrchestratorConfig:
    cfg = AgentOrchestratorConfig()
    cfg.azure_openai_endpoint = os.getenv("AZURE_OPENAI_ENDPOINT", "")
    cfg.azure_openai_deployment = os.getenv("AZURE_OPENAI_DEPLOYMENT", cfg.azure_openai_deployment)
    cfg.azure_openai_api_version = os.getenv("AZURE_OPENAI_API_VERSION", cfg.azure_openai_api_version)
    cfg.use_managed_identity = os.getenv("AZURE_OPENAI_USE_MANAGED_IDENTITY", "true").lower() == "true"
    cfg.api_key = os.getenv("AZURE_OPENAI_API_KEY")

    cfg.max_context_tokens = int(os.getenv("AG_MAX_CONTEXT_TOKENS", str(cfg.max_context_tokens)))
    cfg.system_prompt_reserved_tokens = int(os.getenv("AG_SYSTEM_PROMPT_RESERVED_TOKENS", str(cfg.system_prompt_reserved_tokens)))
    cfg.temperature = float(os.getenv("AG_TEMPERATURE", str(cfg.temperature)))
    cfg.max_completion_tokens = int(os.getenv("AG_MAX_COMPLETION_TOKENS", str(cfg.max_completion_tokens)))

    cfg.content_safety_endpoint = os.getenv("AZURE_CONTENT_SAFETY_ENDPOINT", "")
    cfg.content_safety_api_key = os.getenv("AZURE_CONTENT_SAFETY_API_KEY", "")
    cfg.enable_safety_check = os.getenv("AG_ENABLE_SAFETY", "true").lower() == "true"

    cfg.azure_search_endpoint = os.getenv("AZURE_SEARCH_ENDPOINT", "")
    cfg.search_index_name = os.getenv("AZURE_SEARCH_INDEX_NAME", cfg.search_index_name)
    cfg.rag_top_k = int(os.getenv("AG_RAG_TOP_K", str(cfg.rag_top_k)))

    cfg.application_insights_connection_string = os.getenv("APPLICATIONINSIGHTS_CONNECTION_STRING", "")
    cfg.langsmith_api_key = os.getenv("LANGSMITH_API_KEY")
    cfg.enable_online_evaluation = os.getenv("AG_ENABLE_ONLINE_EVAL", "true").lower() == "true"

    cfg.prompt_version = os.getenv("AG_PROMPT_VERSION", cfg.prompt_version)
    cfg.ab_test_enabled = os.getenv("AG_AB_TEST_ENABLED", "false").lower() == "true"

    cfg.requests_per_minute_per_user = int(os.getenv("AG_REQS_PER_MINUTE", str(cfg.requests_per_minute_per_user)))
    cfg.requests_per_hour_per_user = int(os.getenv("AG_REQS_PER_HOUR", str(cfg.requests_per_hour_per_user)))
    return cfg


@app.route(route="assistant/chat", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def ag_ui_chat(req: func.HttpRequest) -> func.HttpResponse:
    correlation_id = req.headers.get("X-Correlation-Id", str(uuid.uuid4()))
    run_id = uuid.uuid4().hex

    try:
        body = req.get_json()
        request = AgentChatRequest(
            session_id=body["sessionId"],
            submission_id=body["submissionId"],
            user_id=body["userId"],
            user_role=body["userRole"],
            messages=[ChatMessage(**m) for m in body["messages"]],
            correlation_id=correlation_id,
        )
    except (ValueError, KeyError) as e:
        return func.HttpResponse(json.dumps({"error": str(e)}), status_code=400)

    cfg = _config_from_env()
    local_orchestrator = AgentOrchestrator(cfg, audit_sqlite_path=os.getenv("KSQUARE_AGENT_AUDIT_SQLITE_PATH", "agent_audit.sqlite3"))

    async def sse_generator():
        yield f"data: {json.dumps({'type': 'RunStarted', 'runId': run_id, 'correlationId': correlation_id})}\n\n"

        async for chunk in local_orchestrator.chat_stream_async(request):
            if chunk.error:
                yield f"data: {json.dumps({'type': 'RunFinished', 'runId': run_id, 'error': chunk.error, 'done': True})}\n\n"
                yield "data: [DONE]\n\n"
                return

            if chunk.tool_call is not None:
                if chunk.tool_call.result is None and chunk.tool_call.error is None:
                    yield f"data: {json.dumps({'type': 'ToolCall', 'runId': run_id, 'tool': asdict(chunk.tool_call)})}\n\n"
                else:
                    yield f"data: {json.dumps({'type': 'ToolResult', 'runId': run_id, 'tool': asdict(chunk.tool_call)})}\n\n"

            if chunk.delta:
                yield f"data: {json.dumps({'type': 'TextDelta', 'runId': run_id, 'delta': chunk.delta, 'done': False})}\n\n"

            if chunk.is_final:
                payload = {"type": "RunFinished", "runId": run_id, "done": True}
                if chunk.eval_scores:
                    payload["eval"] = asdict(chunk.eval_scores)
                yield f"data: {json.dumps(payload)}\n\n"
                yield "data: [DONE]\n\n"

    return func.HttpResponse(
        body=sse_generator(),
        mimetype="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
            "X-Correlation-Id": correlation_id,
        },
    )


@app.route(route="assistant/feedback", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def ag_ui_feedback(req: func.HttpRequest) -> func.HttpResponse:
    try:
        body = req.get_json()
        feedback = UserFeedback(
            session_id=body["sessionId"],
            turn_id=body["turnId"],
            user_id=body["userId"],
            rating=body["rating"],
            comment=body.get("comment"),
        )
    except (ValueError, KeyError) as e:
        return func.HttpResponse(json.dumps({"error": str(e)}), status_code=400)

    await orchestrator.feedback_handler.record_async(feedback)
    return func.HttpResponse(status_code=204)
