from __future__ import annotations

from ..contracts import IConversationAuditWriter
from ..models import UserFeedback


class FeedbackHandler:
    def __init__(self, audit_writer: IConversationAuditWriter) -> None:
        self._audit_writer = audit_writer

    async def record_async(self, feedback: UserFeedback) -> None:
        await self._audit_writer.write_feedback_async(feedback)

