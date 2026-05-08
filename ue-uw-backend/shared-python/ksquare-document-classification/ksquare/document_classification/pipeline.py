from __future__ import annotations

from typing import Optional

from .config import ClassificationConfig
from .contracts import IDocumentClassifier
from .models import ClassificationMethod, ClassificationResult, DocumentInput
from .providers.azure_classifier import AzureDocumentClassifier
from .providers.heuristic_classifier import HeuristicDocumentClassifier


class AzureThenHeuristicPipeline(IDocumentClassifier):
    def __init__(self, config: ClassificationConfig):
        self._config = config
        self._azure = AzureDocumentClassifier(config)
        self._heuristic = HeuristicDocumentClassifier()

    async def classify_async(
        self, document: DocumentInput, correlation_id: Optional[str] = None
    ) -> ClassificationResult:
        azure_result: ClassificationResult | None = None
        try:
            azure_result = await self._azure.classify_async(document, correlation_id=correlation_id)
        except Exception:
            azure_result = None

        if azure_result and azure_result.confidence >= self._config.confidence_threshold_auto:
            return azure_result

        heuristic_result = await self._heuristic.classify_async(document, correlation_id=correlation_id)

        if azure_result is None:
            return heuristic_result

        if heuristic_result.confidence > azure_result.confidence:
            return heuristic_result

        if azure_result.document_type == "Unknown" and heuristic_result.document_type == "Unknown":
            return ClassificationResult(
                document_type="Unknown",
                confidence=0.0,
                method=ClassificationMethod.MANUAL,
                correlation_id=correlation_id,
            )

        return azure_result
