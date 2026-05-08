from __future__ import annotations

import time

from contracts import AiEmailTriageAdapter, EmailTriageRequest, EmailTriageResult


class MockEmailTriageAdapter(AiEmailTriageAdapter):
    RENEWAL_KEYWORDS = ["renewal", "renew", "expiring policy", "up for renewal"]
    COMPLAINT_KEYWORDS = ["complaint", "unacceptable", "disappointed", "not satisfied"]
    K12_KEYWORDS = ["school district", "k-12", "elementary", "middle school", "high school", "isd", "usd"]
    HIGHER_ED_KEYWORDS = ["university", "college", "community college", "higher ed"]
    URGENCY_KEYWORDS = ["urgent", "asap", "expiring", "deadline", "today", "tomorrow"]

    async def triage_async(self, request: EmailTriageRequest) -> EmailTriageResult:
        start = time.monotonic()
        text = (request.subject + " " + request.body_text).lower()

        intent = "Other"
        if any(kw in text for kw in self.RENEWAL_KEYWORDS):
            intent = "Renewal"
        elif any(kw in text for kw in self.COMPLAINT_KEYWORDS):
            intent = "Complaint"
        elif request.attachment_names:
            intent = "NewSubmission"

        routing = "Manual"
        if any(kw in text for kw in self.K12_KEYWORDS):
            routing = "K12-UW-Queue"
        elif any(kw in text for kw in self.HIGHER_ED_KEYWORDS):
            routing = "HigherEd-UW-Queue"
        elif intent == "Renewal":
            routing = "Renewals-Queue"

        urgency_signals = [kw for kw in self.URGENCY_KEYWORDS if kw in text]
        urgency = "Urgent" if len(urgency_signals) >= 2 else ("High" if urgency_signals else "Normal")
        latency_ms = int((time.monotonic() - start) * 1000)

        return EmailTriageResult(
            email_id=request.email_id,
            intent=intent,
            intent_confidence=0.70,
            extracted_entities=[],
            routing_suggestion=routing,
            urgency=urgency,
            urgency_signals=urgency_signals,
            summary=f"Email from {request.sender_email} — {intent}.",
            model_version="mock",
            prompt_version="mock",
            latency_ms=latency_ms,
            correlation_id=request.correlation_id,
        )

