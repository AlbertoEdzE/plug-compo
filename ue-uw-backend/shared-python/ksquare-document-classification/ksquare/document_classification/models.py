from __future__ import annotations

from enum import Enum
from typing import Optional

from pydantic import BaseModel


class ClassificationMethod(str, Enum):
    AZURE_DOCUMENT_CLASSIFIER = "AzureDocumentClassifier"
    HEURISTIC_KEYWORD = "HeuristicKeyword"
    GPT_VISION = "GptVision"
    MANUAL = "Manual"


class ClassificationCandidate(BaseModel):
    document_type: str
    confidence: float


class ClassificationResult(BaseModel):
    document_type: str
    confidence: float
    method: ClassificationMethod
    alternative_candidates: list[ClassificationCandidate] = []
    correlation_id: Optional[str] = None

    @property
    def requires_manual_review(self) -> bool:
        return self.confidence < 0.70 or self.document_type == "Unknown"


class DocumentInput(BaseModel):
    blob_path: Optional[str] = None
    document_uri: Optional[str] = None
    content_base64: Optional[str] = None

    content_type: str
    file_name: Optional[str] = None
    first_page_text: Optional[str] = None
