from __future__ import annotations

import json
import time
from typing import Callable, Awaitable

from ..contracts import IToolRouter
from ..models import ToolResult
from .checklist_tools import get_checklist_status
from .data_store import SynthesizedSubmissionStore
from .document_tools import get_document_excerpt
from .loss_tools import get_loss_history
from .risk_tools import get_risk_indicators
from .submission_tools import get_coverage_summary, get_submission_summary


class ToolRouter(IToolRouter):
    def __init__(self, store: SynthesizedSubmissionStore | None = None) -> None:
        self._store = store or SynthesizedSubmissionStore()
        self._tools: dict[str, Callable[..., Awaitable[ToolResult]]] = {
            "get_submission_summary": lambda submission_id, **_: get_submission_summary(self._store, submission_id),
            "get_coverage_summary": lambda submission_id, **_: get_coverage_summary(self._store, submission_id),
            "get_loss_history": lambda submission_id, years=5, **_: get_loss_history(self._store, submission_id, years=years),
            "get_risk_indicators": lambda submission_id, **_: get_risk_indicators(self._store, submission_id),
            "get_checklist_status": lambda submission_id, **_: get_checklist_status(self._store, submission_id),
            "get_document_excerpt": lambda submission_id, query, document_type=None, **_: get_document_excerpt(
                submission_id=submission_id,
                query=query,
                document_type=document_type,
            ),
        }

    async def execute_async(self, tool_name: str, arguments: dict, submission_id: str) -> ToolResult:
        tool = self._tools.get(tool_name)
        if tool is None:
            return ToolResult(success=False, content="", raw_data=None, error=f"Unknown tool: {tool_name}")

        args = dict(arguments or {})
        args.setdefault("submission_id", submission_id)

        start = time.perf_counter()
        try:
            result = await tool(**args)
        except Exception as ex:
            return ToolResult(success=False, content="", raw_data=None, error=str(ex))
        finally:
            _ = int((time.perf_counter() - start) * 1000)

        if result.raw_data is None and result.content:
            try:
                result.raw_data = json.loads(result.content)
            except Exception:
                pass

        return result

