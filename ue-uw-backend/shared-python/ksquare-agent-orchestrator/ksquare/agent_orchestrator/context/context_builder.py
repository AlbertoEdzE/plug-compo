from __future__ import annotations

import hashlib

from ..contracts import IAssistantContextBuilder
from ..models import AssistantContext, UserContext


class AssistantContextBuilder(IAssistantContextBuilder):
    async def build_async(self, submission_id: str, user_context: UserContext) -> AssistantContext:
        seed = int(hashlib.md5(submission_id.encode("utf-8")).hexdigest(), 16) % 10_000
        submission_number = f"SUB-{seed:04d}"
        institution_name = f"Institution {seed}"
        status = "InReview"

        formatted = "\n".join(
            [
                f"SubmissionId: {submission_id}",
                f"SubmissionNumber: {submission_number}",
                f"InstitutionName: {institution_name}",
                f"Status: {status}",
                "CoverageLines:",
                "- GL: limit $1,000,000",
            ]
        )

        return AssistantContext(
            submission_id=submission_id,
            submission_number=submission_number,
            institution_name=institution_name,
            institution_type="K12",
            location="New York, NY",
            status=status,
            effective_date="2026-01-01",
            broker_name="Broker 1",
            coverage_lines=[{"line": "GL", "limit": 1000000, "premium": 25000}],
            loss_history_summary=None,
            risk_indicators=None,
            appetite_fit_score=None,
            documents=[],
            formatted_context_block=formatted,
        )

