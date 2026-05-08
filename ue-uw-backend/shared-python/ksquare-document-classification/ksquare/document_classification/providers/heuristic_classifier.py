from __future__ import annotations

from typing import Optional

from ..contracts import IDocumentClassifier
from ..models import ClassificationMethod, ClassificationResult, DocumentInput


KEYWORD_RULES: dict[str, list[str]] = {
    "ACORD125": ["acord 125", "commercial lines application", "acord125"],
    "ACORD126": ["acord 126", "acord126", "commercial general liability"],
    "LossRun": ["loss run", "claims history", "prior losses", "loss history"],
    "FinancialStatement": ["balance sheet", "profit and loss", "income statement", "p&l"],
    "PropertySchedule": ["property schedule", "schedule of locations", "building schedule"],
    "Certificate": ["certificate of insurance", "acord 25", "acord25"],
}


class HeuristicDocumentClassifier(IDocumentClassifier):
    async def classify_async(
        self, document: DocumentInput, correlation_id: Optional[str] = None
    ) -> ClassificationResult:
        text_signal = (document.file_name or "").lower()
        if document.first_page_text:
            text_signal += " " + document.first_page_text[:500].lower()

        best_type = "Unknown"
        best_score = 0.0

        for doc_type, keywords in KEYWORD_RULES.items():
            hits = sum(1 for kw in keywords if kw in text_signal)
            score = min(hits / len(keywords), 1.0) * 0.80

            if score > best_score:
                best_score = score
                best_type = doc_type

        confidence = best_score if best_score > 0 else 0.0
        doc_type = best_type if confidence >= 0.40 else "Unknown"

        return ClassificationResult(
            document_type=doc_type,
            confidence=confidence,
            method=ClassificationMethod.HEURISTIC_KEYWORD,
            correlation_id=correlation_id,
        )
