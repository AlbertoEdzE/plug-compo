from __future__ import annotations

import json

from ..models import ToolResult
from .data_store import SynthesizedSubmissionStore


async def get_submission_summary(store: SynthesizedSubmissionStore, submission_id: str) -> ToolResult:
    data = store.submission_summary(submission_id)
    return ToolResult(success=True, content=json.dumps(data), raw_data=data)


async def get_coverage_summary(store: SynthesizedSubmissionStore, submission_id: str) -> ToolResult:
    data = store.coverage_summary(submission_id)
    return ToolResult(success=True, content=json.dumps(data), raw_data=data)

