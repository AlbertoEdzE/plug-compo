from __future__ import annotations

from enum import Enum
from typing import Any, Optional

from pydantic import BaseModel, Field, model_validator


class ExtractionStatus(str, Enum):
    SUCCEEDED = "Succeeded"
    PARTIAL_RESULTS = "PartialResults"
    FAILED = "Failed"
    PENDING_REVIEW = "PendingReview"


class BoundingBox(BaseModel):
    x: float
    y: float
    width: float
    height: float
    page: int


class DocumentInput(BaseModel):
    blob_path: Optional[str] = None
    document_uri: Optional[str] = None
    content_base64: Optional[str] = None

    content_type: str
    file_name: Optional[str] = None

    @model_validator(mode="after")
    def _validate_one_source(self) -> "DocumentInput":
        sources = [
            bool(self.blob_path),
            bool(self.document_uri),
            bool(self.content_base64),
        ]
        if sum(1 for x in sources if x) != 1:
            raise ValueError("Exactly one of blob_path, document_uri, or content_base64 must be set.")
        return self


class ExtractedField(BaseModel):
    name: str
    value: Optional[str]
    confidence: float
    bounding_box: Optional[BoundingBox] = None
    page_number: Optional[int] = None

    @property
    def needs_review(self) -> bool:
        return self.confidence < 0.75


class ExtractedTable(BaseModel):
    table_name: str
    page_number: int
    headers: list[str]
    rows: list[list[Optional[str]]]
    confidence: float = 0.0


class ExtractedPage(BaseModel):
    page_number: int
    width: int
    height: int
    unit: str


class ExtractionResult(BaseModel):
    document_id: str
    provider_operation_id: str
    status: ExtractionStatus
    fields: list[ExtractedField]
    tables: list[ExtractedTable]
    pages: list[ExtractedPage]

    detected_document_type: Optional[str] = None
    overall_confidence: float = 0.0
    extracted_at: Optional[str] = None
    model_used: Optional[str] = None
    correlation_id: Optional[str] = None

    metadata: dict[str, Any] = Field(default_factory=dict)
