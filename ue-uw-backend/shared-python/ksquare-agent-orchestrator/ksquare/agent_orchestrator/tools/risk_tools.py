from __future__ import annotations

import json

from ..models import ToolResult
from .data_store import SynthesizedSubmissionStore


async def get_risk_indicators(store: SynthesizedSubmissionStore, submission_id: str) -> ToolResult:
    data = store.risk_indicators(submission_id)
    return ToolResult(success=True, content=json.dumps(data), raw_data=data)

