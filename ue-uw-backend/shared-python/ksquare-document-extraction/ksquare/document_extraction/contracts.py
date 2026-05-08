from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Optional

from .models import DocumentInput, ExtractionResult


class IDocumentExtractor(ABC):
    @abstractmethod
    async def extract_async(
        self,
        document: DocumentInput,
        model_hint: Optional[str] = None,
        correlation_id: Optional[str] = None,
    ) -> ExtractionResult:
        ...
