from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional


@dataclass
class ChatMessage:
    role: str
    content: str
    tool_call_id: Optional[str] = None
    tool_name: Optional[str] = None


@dataclass
class AgentChatRequest:
    session_id: str
    submission_id: str
    user_id: str
    user_role: str
    messages: list[ChatMessage]
    correlation_id: Optional[str] = None


@dataclass
class ToolCallEvent:
    tool_name: str
    arguments: dict
    result: Optional[str] = None
    error: Optional[str] = None
    duration_ms: Optional[int] = None


@dataclass
class EvaluationScores:
    groundedness: Optional[float] = None
    answer_relevance: Optional[float] = None
    context_relevance: Optional[float] = None
    faithfulness: Optional[float] = None
    latency_ms: Optional[int] = None
    prompt_tokens: Optional[int] = None
    completion_tokens: Optional[int] = None
    estimated_cost_usd: Optional[float] = None


@dataclass
class AgentStreamChunk:
    delta: str
    is_final: bool = False
    tool_call: Optional[ToolCallEvent] = None
    error: Optional[str] = None
    eval_scores: Optional[EvaluationScores] = None


@dataclass
class AssistantContext:
    submission_id: str
    submission_number: str
    institution_name: str
    institution_type: str
    location: str
    status: str
    effective_date: str
    broker_name: str
    coverage_lines: list[dict]
    loss_history_summary: Optional[str]
    risk_indicators: Optional[dict]
    appetite_fit_score: Optional[float]
    documents: list[dict]
    formatted_context_block: str


@dataclass
class UserContext:
    user_id: str
    user_role: str
    display_name: str


@dataclass
class ToolResult:
    success: bool
    content: str
    raw_data: Optional[dict] = None
    error: Optional[str] = None


@dataclass
class SafetyCheckResult:
    passed: bool
    category: Optional[str] = None
    score: Optional[float] = None


@dataclass
class ConversationTurn:
    turn_id: str
    session_id: str
    submission_id: str
    user_id: str
    role: str
    content_hash: str
    content_redacted: str
    model_used: str
    prompt_tokens: int
    completion_tokens: int
    latency_ms: int
    finish_reason: str
    tool_calls: list[dict]
    eval_scores: Optional[EvaluationScores] = None
    created_at: datetime = field(default_factory=datetime.utcnow)


@dataclass
class UserFeedback:
    session_id: str
    turn_id: str
    user_id: str
    rating: str
    comment: Optional[str] = None
    created_at: datetime = field(default_factory=datetime.utcnow)

