from __future__ import annotations

import json

from ..models import ToolResult
from .data_store import SynthesizedSubmissionStore


async def get_loss_history(store: SynthesizedSubmissionStore, submission_id: str, years: int = 5) -> ToolResult:
    data = store.loss_history(submission_id, years=years)
    return ToolResult(success=True, content=json.dumps(data), raw_data=data)

