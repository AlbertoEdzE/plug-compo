from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Optional

from .models import ClassificationResult, DocumentInput


class IDocumentClassifier(ABC):
    @abstractmethod
    async def classify_async(
        self,
        document: DocumentInput,
        correlation_id: Optional[str] = None,
    ) -> ClassificationResult:
        ...
