from __future__ import annotations

import json
import time
import uuid
from typing import AsyncIterator

from .audit.audit_db_context import AuditDbContext
from .audit.conversation_audit_writer import SqliteConversationAuditWriter
from .config import AgentOrchestratorConfig
from .context.context_builder import AssistantContextBuilder
from .context.token_budget import trim_messages_to_budget
from .contracts import (
    IAgentOrchestrator,
    IAssistantContextBuilder,
    IConversationAuditWriter,
    IEvaluationScorer,
    ISafetyGuard,
    IToolRouter,
)
from .evaluation.online_scorer import OnlineEvaluationScorer
from .feedback.feedback_handler import FeedbackHandler
from .models import (
    AgentChatRequest,
    AgentStreamChunk,
    ChatMessage,
    ConversationTurn,
    EvaluationScores,
    ToolCallEvent,
    UserContext,
)
from .observability.llm_tracer import LlmTracer
from .policy.prompt_policy_enforcer import PromptPolicyEnforcer
from .policy.prompt_version_manager import PromptVersionManager
from .safety.safety_guard import PatternSafetyGuard
from .tools.tool_router import ToolRouter


class AgentOrchestrator(IAgentOrchestrator):
    def __init__(
        self,
        config: AgentOrchestratorConfig,
        *,
        safety_guard: ISafetyGuard | None = None,
        context_builder: IAssistantContextBuilder | None = None,
        tool_router: IToolRouter | None = None,
        evaluation_scorer: IEvaluationScorer | None = None,
        audit_writer: IConversationAuditWriter | None = None,
        audit_sqlite_path: str = "agent_audit.sqlite3",
    ) -> None:
        self._config = config
        self._safety_guard = safety_guard or PatternSafetyGuard()
        self._context_builder = context_builder or AssistantContextBuilder()
        self._tool_router = tool_router or ToolRouter()
        self._evaluation_scorer = evaluation_scorer or OnlineEvaluationScorer()
        self._prompt_policy_enforcer = PromptPolicyEnforcer()
        self._prompt_version_manager = PromptVersionManager()
        self._tracer = LlmTracer()

        self._audit_writer = audit_writer or SqliteConversationAuditWriter(AuditDbContext(audit_sqlite_path))
        self.feedback_handler = FeedbackHandler(self._audit_writer)

        self._rate_limiter = _InMemoryRateLimiter(
            per_minute=config.requests_per_minute_per_user,
            per_hour=config.requests_per_hour_per_user,
        )

    async def chat_stream_async(self, request: AgentChatRequest) -> AsyncIterator[AgentStreamChunk]:
        if not self._rate_limiter.allow(request.user_id):
            yield AgentStreamChunk(delta="", is_final=True, error="Rate limit exceeded.")
            return

        last_user_message = next((m.content for m in reversed(request.messages) if m.role == "user"), "")
        if self._config.enable_safety_check:
            safety = await self._safety_guard.check_input_async(last_user_message)
            if not safety.passed:
                yield AgentStreamChunk(delta="", is_final=True, error=f"Safety check failed: {safety.category}")
                return

        user_context = UserContext(
            user_id=request.user_id,
            user_role=request.user_role,
            display_name=request.user_id,
        )

        context = await self._context_builder.build_async(request.submission_id, user_context)

        _ = self._prompt_version_manager.select_version(request.session_id)
        prepared_messages = self._prompt_policy_enforcer.enforce(request.messages, context, user_context)
        prepared_messages = trim_messages_to_budget(
            prepared_messages,
            system_reserved_tokens=self._config.system_prompt_reserved_tokens,
            max_context_tokens=self._config.max_context_tokens,
        )

        tool_events: list[ToolCallEvent] = []
        retrieved_docs: list[str] = []

        tool_to_call = self._select_tool_for_question(last_user_message)
        tool_result_payload: dict | None = None
        if tool_to_call is not None:
            tool_call = ToolCallEvent(tool_name=tool_to_call, arguments={"submission_id": request.submission_id})
            yield AgentStreamChunk(delta="", tool_call=tool_call)

            tool_start = time.perf_counter()
            result = await self._tool_router.execute_async(tool_to_call, tool_call.arguments, request.submission_id)
            tool_call.duration_ms = int((time.perf_counter() - tool_start) * 1000)
            tool_call.result = result.content if result.success else None
            tool_call.error = result.error if not result.success else None
            tool_events.append(tool_call)

            yield AgentStreamChunk(delta="", tool_call=tool_call)

            if result.raw_data is not None:
                tool_result_payload = result.raw_data
            else:
                try:
                    tool_result_payload = json.loads(result.content) if result.content else None
                except Exception:
                    tool_result_payload = None

        model_used = self._config.azure_openai_deployment
        with self._tracer.llm_span(model=model_used, operation="chat") as span:
            start = time.perf_counter()
            answer = self._synthesize_answer(prepared_messages, context.formatted_context_block, tool_to_call, tool_result_payload)
            latency_ms = int((time.perf_counter() - start) * 1000)

            prompt_tokens = max(len(_serialize_messages(prepared_messages)) // 4, 1)
            completion_tokens = max(len(answer) // 4, 1)
            estimated_cost = self._tracer.record_usage(span, prompt_tokens, completion_tokens, model_used)

        if self._config.enable_safety_check:
            safety_resp = await self._safety_guard.check_response_async(answer, context)
            if not safety_resp.passed:
                yield AgentStreamChunk(delta="", is_final=True, error=f"Safety check failed: {safety_resp.category}")
                return

        eval_scores: EvaluationScores | None = None
        if self._config.enable_online_evaluation:
            eval_scores = await self._evaluation_scorer.score_async(
                question=last_user_message,
                answer=answer,
                context=context.formatted_context_block,
                retrieved_docs=retrieved_docs,
            )
            eval_scores.latency_ms = latency_ms
            eval_scores.prompt_tokens = prompt_tokens
            eval_scores.completion_tokens = completion_tokens
            eval_scores.estimated_cost_usd = estimated_cost

        turn = ConversationTurn(
            turn_id=uuid.uuid4().hex,
            session_id=request.session_id,
            submission_id=request.submission_id,
            user_id=request.user_id,
            role="assistant",
            content_hash="",
            content_redacted=answer,
            model_used=model_used,
            prompt_tokens=prompt_tokens,
            completion_tokens=completion_tokens,
            latency_ms=latency_ms,
            finish_reason="stop",
            tool_calls=[_tool_call_to_dict(t) for t in tool_events],
            eval_scores=eval_scores,
        )
        await self._audit_writer.write_turn_async(turn)

        for chunk in _chunk_text(answer):
            yield AgentStreamChunk(delta=chunk)

        yield AgentStreamChunk(delta="", is_final=True, eval_scores=eval_scores)

    @staticmethod
    def _select_tool_for_question(question: str) -> str | None:
        q = (question or "").lower()
        if "loss ratio" in q or "loss history" in q or "prior loss" in q:
            return "get_loss_history"
        if "risk" in q or "appetite" in q:
            return "get_risk_indicators"
        if "coverage" in q or "limit" in q or "premium" in q:
            return "get_coverage_summary"
        if "missing" in q or "checklist" in q:
            return "get_checklist_status"
        return None

    @staticmethod
    def _synthesize_answer(
        messages: list[ChatMessage],
        context_block: str,
        tool_name: str | None,
        tool_payload: dict | None,
    ) -> str:
        question = next((m.content for m in reversed(messages) if m.role == "user"), "")
        lines = []
        if tool_name == "get_loss_history" and tool_payload and "history" in tool_payload:
            history = tool_payload["history"]
            if history:
                last = history[0]
                lines.append(f"From the loss run: {last['year']} loss ratio was {last['loss_ratio']:.2f}.")
        elif tool_name == "get_risk_indicators" and tool_payload:
            lines.append("Risk indicators:")
            for k, v in tool_payload.items():
                if k == "submission_id":
                    continue
                lines.append(f"- {k}: {v}")
        elif tool_name == "get_coverage_summary" and tool_payload:
            lines.append("Coverage summary:")
            for line in tool_payload.get("coverage_lines", []):
                lines.append(f"- {line.get('line')}: limit {line.get('limit')}, premium {line.get('premium')}")
        elif tool_name == "get_checklist_status" and tool_payload:
            missing = tool_payload.get("missing", [])
            if missing:
                lines.append("Missing documents:")
                for m in missing:
                    lines.append(f"- {m}")
            else:
                lines.append("No missing documents are flagged in the checklist.")
        else:
            lines.append("I can help with this submission based on the current context.")

        if not lines:
            lines.append("I do not have enough information in the current context to answer.")

        if question:
            lines.append("")
            lines.append(f"Question: {question}")
        lines.append("")
        lines.append("Context snapshot:")
        lines.append(context_block.strip())
        return "\n".join(lines).strip()


class _InMemoryRateLimiter:
    def __init__(self, *, per_minute: int, per_hour: int) -> None:
        self._per_minute = per_minute
        self._per_hour = per_hour
        self._events: dict[str, list[float]] = {}

    def allow(self, user_id: str) -> bool:
        now = time.time()
        events = self._events.setdefault(user_id, [])
        events[:] = [t for t in events if t >= now - 3600]

        last_minute = [t for t in events if t >= now - 60]
        if len(last_minute) >= self._per_minute:
            return False
        if len(events) >= self._per_hour:
            return False

        events.append(now)
        return True


def _chunk_text(text: str, chunk_size: int = 32) -> list[str]:
    if not text:
        return []
    return [text[i : i + chunk_size] for i in range(0, len(text), chunk_size)]


def _serialize_messages(messages: list[ChatMessage]) -> str:
    return "\n".join(f"{m.role}:{m.content}" for m in messages)


def _tool_call_to_dict(t: ToolCallEvent) -> dict:
    return {
        "tool_name": t.tool_name,
        "arguments": t.arguments,
        "result": t.result,
        "error": t.error,
        "duration_ms": t.duration_ms,
    }
