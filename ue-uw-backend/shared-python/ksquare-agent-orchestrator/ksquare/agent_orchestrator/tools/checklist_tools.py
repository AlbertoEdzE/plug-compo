from __future__ import annotations

import json

from ..models import ToolResult
from .data_store import SynthesizedSubmissionStore


async def get_checklist_status(store: SynthesizedSubmissionStore, submission_id: str) -> ToolResult:
    data = store.checklist_status(submission_id)
    return ToolResult(success=True, content=json.dumps(data), raw_data=data)

