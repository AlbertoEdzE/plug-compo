from __future__ import annotations

import asyncio
import base64
import uuid
from typing import Optional

from azure.ai.documentintelligence import DocumentIntelligenceClient
from azure.ai.documentintelligence.models import AnalyzeDocumentRequest
from azure.core.credentials import AzureKeyCredential
from azure.identity import DefaultAzureCredential

from ..config import ExtractionConfig
from ..contracts import IDocumentExtractor
from ..models import BoundingBox, DocumentInput, ExtractedField, ExtractedPage, ExtractedTable, ExtractionResult, ExtractionStatus
from ..routing import resolve_model_id


class AzureDocumentExtractor(IDocumentExtractor):
    def __init__(self, config: ExtractionConfig):
        if config.use_managed_identity:
            credential = DefaultAzureCredential()
        else:
            if not config.api_key:
                raise ValueError("api_key is required when use_managed_identity is False.")
            credential = AzureKeyCredential(config.api_key)

        self._config = config
        self._client = DocumentIntelligenceClient(config.endpoint, credential)

    async def extract_async(
        self,
        document: DocumentInput,
        model_hint: Optional[str] = None,
        correlation_id: Optional[str] = None,
    ) -> ExtractionResult:
        model_id = resolve_model_id(self._config, model_hint)
        document_id = correlation_id or str(uuid.uuid4())

        request = self._build_request(document)
        poller = self._client.begin_analyze_document(model_id=model_id, body=request)

        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(None, poller.result)

        fields = self._map_fields(result)
        tables = self._map_tables(result)
        pages = self._map_pages(result)

        overall_confidence = sum(f.confidence for f in fields) / len(fields) if fields else 0.0
        status = self._compute_status(fields, tables)

        return ExtractionResult(
            document_id=document_id,
            provider_operation_id=getattr(poller, "id", "unknown"),
            status=status,
            fields=fields,
            tables=tables,
            pages=pages,
            overall_confidence=overall_confidence,
            model_used=model_id,
            correlation_id=correlation_id,
        )

    def _build_request(self, document: DocumentInput) -> AnalyzeDocumentRequest:
        if document.document_uri:
            return AnalyzeDocumentRequest(url_source=document.document_uri)

        if document.content_base64:
            return AnalyzeDocumentRequest(bytes_source=base64.b64decode(document.content_base64))

        return AnalyzeDocumentRequest(url_source=document.blob_path)

    def _compute_status(self, fields: list[ExtractedField], tables: list[ExtractedTable]) -> ExtractionStatus:
        if not fields and not tables:
            return ExtractionStatus.FAILED

        if any(f.confidence < self._config.low_confidence_threshold for f in fields):
            return ExtractionStatus.PENDING_REVIEW

        return ExtractionStatus.SUCCEEDED

    def _map_fields(self, analysis_result) -> list[ExtractedField]:
        mapped: list[ExtractedField] = []

        for doc in getattr(analysis_result, "documents", None) or []:
            for name, field in (getattr(doc, "fields", None) or {}).items():
                confidence = float(getattr(field, "confidence", 0.0) or 0.0)
                content = getattr(field, "content", None)

                page_number = None
                bbox = None
                regions = getattr(field, "bounding_regions", None) or []
                if regions:
                    page_number = getattr(regions[0], "page_number", None)
                    bbox = self._to_bbox(regions)

                mapped.append(
                    ExtractedField(
                        name=str(name),
                        value=content,
                        confidence=confidence,
                        page_number=page_number,
                        bounding_box=bbox,
                    )
                )

        return mapped

    def _map_tables(self, analysis_result) -> list[ExtractedTable]:
        mapped: list[ExtractedTable] = []

        tables = getattr(analysis_result, "tables", None) or []
        for i, table in enumerate(tables):
            cells = list(getattr(table, "cells", None) or [])
            if not cells:
                continue

            max_row = max(getattr(c, "row_index", 0) for c in cells)
            max_col = max(getattr(c, "column_index", 0) for c in cells)

            grid: list[list[Optional[str]]] = [
                [None for _ in range(max_col + 1)]
                for _ in range(max_row + 1)
            ]

            for cell in cells:
                r = getattr(cell, "row_index", 0)
                c = getattr(cell, "column_index", 0)
                grid[r][c] = getattr(cell, "content", None)

            headers = [h or "" for h in grid[0]]
            rows = grid[1:]

            page_number = 1
            regions = getattr(table, "bounding_regions", None) or []
            if regions:
                page_number = getattr(regions[0], "page_number", 1) or 1

            mapped.append(
                ExtractedTable(
                    table_name=f"table_{i}",
                    page_number=page_number,
                    headers=headers,
                    rows=rows,
                    confidence=0.9,
                )
            )

        return mapped

    def _map_pages(self, analysis_result) -> list[ExtractedPage]:
        pages = getattr(analysis_result, "pages", None) or []
        mapped: list[ExtractedPage] = []
        for p in pages:
            mapped.append(
                ExtractedPage(
                    page_number=int(getattr(p, "page_number", 1) or 1),
                    width=int(getattr(p, "width", 0) or 0),
                    height=int(getattr(p, "height", 0) or 0),
                    unit=str(getattr(p, "unit", "")),
                )
            )
        return mapped

    @staticmethod
    def _to_bbox(bounding_regions) -> Optional[BoundingBox]:
        polygons = []
        page = None

        for region in bounding_regions or []:
            page = page or getattr(region, "page_number", None)
            polygon = getattr(region, "polygon", None) or []
            if polygon:
                polygons.extend(polygon)

        if not polygons or page is None:
            return None

        xs = [float(p.x) for p in polygons]
        ys = [float(p.y) for p in polygons]
        min_x, max_x = min(xs), max(xs)
        min_y, max_y = min(ys), max(ys)

        return BoundingBox(
            x=min_x,
            y=min_y,
            width=max_x - min_x,
            height=max_y - min_y,
            page=int(page),
        )
