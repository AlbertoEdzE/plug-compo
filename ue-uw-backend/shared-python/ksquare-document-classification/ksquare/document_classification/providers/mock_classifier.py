from __future__ import annotations

from typing import Optional

from ..contracts import IDocumentClassifier
from ..models import ClassificationMethod, ClassificationResult, DocumentInput


class MockDocumentClassifier(IDocumentClassifier):
    def __init__(self, result: ClassificationResult | None = None):
        self._result = result or ClassificationResult(
            document_type="ACORD125",
            confidence=0.92,
            method=ClassificationMethod.AZURE_DOCUMENT_CLASSIFIER,
        )

    async def classify_async(
        self, document: DocumentInput, correlation_id: Optional[str] = None
    ) -> ClassificationResult:
        result = self._result.model_copy(deep=True)
        result.correlation_id = correlation_id
        return result
