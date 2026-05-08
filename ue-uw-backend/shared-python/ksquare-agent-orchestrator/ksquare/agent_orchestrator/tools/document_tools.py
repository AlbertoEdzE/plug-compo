from __future__ import annotations

import json

from ..models import ToolResult


async def get_document_excerpt(submission_id: str, query: str, document_type: str | None = None) -> ToolResult:
    data = {
        "submission_id": submission_id,
        "query": query,
        "document_type": document_type,
        "excerpt": f"Excerpt for '{query}' (document_type={document_type})",
        "page_number": 1,
    }
    return ToolResult(success=True, content=json.dumps(data), raw_data=data)

