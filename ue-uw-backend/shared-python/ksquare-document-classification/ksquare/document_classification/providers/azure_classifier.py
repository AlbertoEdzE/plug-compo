from __future__ import annotations

import asyncio
import base64
from typing import Optional

from azure.ai.documentintelligence import DocumentIntelligenceClient
from azure.ai.documentintelligence.models import ClassifyDocumentRequest
from azure.core.credentials import AzureKeyCredential
from azure.identity import DefaultAzureCredential

from ..config import ClassificationConfig
from ..contracts import IDocumentClassifier
from ..models import ClassificationMethod, ClassificationResult, DocumentInput


class AzureDocumentClassifier(IDocumentClassifier):
    def __init__(self, config: ClassificationConfig):
        if not config.azure_endpoint:
            raise ValueError("azure_endpoint is required for AzureDocumentClassifier.")

        if config.use_managed_identity:
            credential = DefaultAzureCredential()
        else:
            api_key = getattr(config, "azure_api_key", None)
            if not api_key:
                raise ValueError("azure_api_key is required when use_managed_identity is False.")
            credential = AzureKeyCredential(api_key)

        self._config = config
        self._client = DocumentIntelligenceClient(config.azure_endpoint, credential)

    async def classify_async(
        self, document: DocumentInput, correlation_id: Optional[str] = None
    ) -> ClassificationResult:
        request = self._build_request(document)
        poller = self._client.begin_classify_document(
            model_id=self._config.azure_classifier_model_id,
            body=request,
        )

        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(None, poller.result)

        doc = (getattr(result, "documents", None) or [None])[0]
        if doc is None:
            return ClassificationResult(
                document_type="Unknown",
                confidence=0.0,
                method=ClassificationMethod.AZURE_DOCUMENT_CLASSIFIER,
                correlation_id=correlation_id,
            )

        doc_type = getattr(doc, "doc_type", None) or getattr(doc, "docType", None) or "Unknown"
        confidence = float(getattr(doc, "confidence", 0.0) or 0.0)

        return ClassificationResult(
            document_type=str(doc_type),
            confidence=confidence,
            method=ClassificationMethod.AZURE_DOCUMENT_CLASSIFIER,
            correlation_id=correlation_id,
        )

    @staticmethod
    def _build_request(document: DocumentInput) -> ClassifyDocumentRequest:
        if document.document_uri:
            return ClassifyDocumentRequest(url_source=document.document_uri)

        if document.content_base64:
            return ClassifyDocumentRequest(bytes_source=base64.b64decode(document.content_base64))

        return ClassifyDocumentRequest(url_source=document.blob_path)
