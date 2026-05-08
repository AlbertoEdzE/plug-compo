from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Optional

from ..contracts import IDocumentExtractor
from ..models import DocumentInput, ExtractionResult


class MockDocumentExtractor(IDocumentExtractor):
    def __init__(self, fixture_path: str | None = None):
        self._fixture_path = fixture_path or os.getenv("KSQUARE_DOC_EXTRACT_FIXTURE_PATH")

    async def extract_async(
        self,
        document: DocumentInput,
        model_hint: Optional[str] = None,
        correlation_id: Optional[str] = None,
    ) -> ExtractionResult:
        fixture = self._resolve_fixture_path()
        payload = json.loads(fixture.read_text(encoding="utf-8"))
        result = ExtractionResult.model_validate(payload)

        if correlation_id:
            result.correlation_id = correlation_id
            result.document_id = correlation_id

        if model_hint:
            result.model_used = model_hint

        return result

    def _resolve_fixture_path(self) -> Path:
        if self._fixture_path:
            return Path(self._fixture_path)

        here = Path(__file__).resolve()
        default_path = here.parents[3] / "tests" / "fixtures" / "sample_acord125.json"
        return default_path
