from __future__ import annotations

from ..contracts import ISafetyGuard
from ..models import AssistantContext, SafetyCheckResult


PROMPT_INJECTION_PATTERNS = [
    "ignore previous instructions",
    "ignore your system prompt",
    "you are now",
    "act as if you are",
    "disregard all previous",
    "system:",
    "new instruction:",
    "override your rules",
]

OUT_OF_SCOPE_PATTERNS = [
    "approve this",
    "decline this",
    "bind this",
    "issue the quote",
    "change the status",
    "update the field",
    "modify the submission",
]


class PatternSafetyGuard(ISafetyGuard):
    async def check_input_async(self, text: str) -> SafetyCheckResult:
        lower = (text or "").lower()
        for pattern in PROMPT_INJECTION_PATTERNS:
            if pattern in lower:
                return SafetyCheckResult(passed=False, category="prompt_injection", score=1.0)

        for pattern in OUT_OF_SCOPE_PATTERNS:
            if pattern in lower:
                return SafetyCheckResult(passed=False, category="out_of_scope", score=0.9)

        return SafetyCheckResult(passed=True)

    async def check_response_async(self, text: str, context: AssistantContext) -> SafetyCheckResult:
        lower = (text or "").lower()
        for pattern in OUT_OF_SCOPE_PATTERNS:
            if pattern in lower:
                return SafetyCheckResult(passed=False, category="response_out_of_scope", score=0.8)

        return SafetyCheckResult(passed=True)

