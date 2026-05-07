# Component 10 — Document Extraction Adapter

**Library**: `KSquare.DocumentExtraction`  
**Layer**: Intelligence  
**Default Provider**: Azure AI Document Intelligence (Form Recognizer)  
**Alternate Providers**: AWS Textract, Google Document AI, Tesseract OCR (local, lower fidelity)  
**Language**: Python 3.11 (primary — used in IDP Azure Function) + C# .NET 8 (SDK wrapper for services)

---

## Why This Is a Pluggable Component

Document extraction is the core intelligence step in the IDP (Intelligent Document Processing) pipeline.
Raw PDFs and image files must be converted into structured field-value pairs before any submission
data can be populated or reviewed.

Complexity justifying a library:
- Multiple document types require different analysis models (prebuilt vs. custom trained)
- Confidence scoring per field (low-confidence fields flagged for human review)
- Multi-page document handling (aggregate results across pages)
- Retry and backoff for Azure's asynchronous long-running operation model
- Bounding box coordinates preserved for UI document annotation overlay
- Key-value pair extraction + table extraction (loss run schedules, property schedules)
- Provider abstraction: Azure, AWS Textract, and fallback OCR all return very different shapes

---

## Interface Contract (Python)

```python
# ksquare/document_extraction/contracts.py

from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Optional

class IDocumentExtractor(ABC):
    @abstractmethod
    async def extract_async(
        self,
        document: "DocumentInput",
        model_hint: Optional[str] = None,
        correlation_id: Optional[str] = None
    ) -> "ExtractionResult":
        """Extract structured fields from a document."""
        ...
```

---

## Interface Contract (C# — for service-side consumption)

```csharp
namespace KSquare.DocumentExtraction.Contracts;

public interface IDocumentExtractor
{
    // Trigger extraction on a blob-stored document.
    // Returns ExtractionResult with all fields and confidence scores.
    Task<ExtractionResult> ExtractAsync(
        DocumentInput input,
        string? modelHint = null,
        CancellationToken ct = default);
}
```

---

## Models (C#)

```csharp
namespace KSquare.DocumentExtraction.Models;

public record DocumentInput
{
    // Exactly one of the following must be set:
    public string? BlobPath { get; init; }           // fetched via IBlobStorageConnector
    public Uri? DocumentUri { get; init; }            // direct SAS URL
    public byte[]? Content { get; init; }             // inline bytes

    public required string ContentType { get; init; } // "application/pdf", "image/jpeg"
    public string? FileName { get; init; }
}

public record ExtractionResult
{
    public required string DocumentId { get; init; }
    public required string ProviderOperationId { get; init; }
    public required ExtractionStatus Status { get; init; }
    public required IReadOnlyList<ExtractedField> Fields { get; init; }
    public required IReadOnlyList<ExtractedTable> Tables { get; init; }
    public required IReadOnlyList<ExtractedPage> Pages { get; init; }
    public string? DetectedDocumentType { get; init; }  // "ACORD125", "LossRun", "Financial"
    public float OverallConfidence { get; init; }        // average of all field confidences
    public DateTimeOffset ExtractedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? ModelUsed { get; init; }
    public string? CorrelationId { get; init; }
}

public record ExtractedField
{
    public required string Name { get; init; }           // canonical field name
    public required string? Value { get; init; }
    public required float Confidence { get; init; }      // 0.0 – 1.0
    public BoundingBox? BoundingBox { get; init; }       // for UI overlay
    public int? PageNumber { get; init; }
    public bool NeedsReview => Confidence < 0.75f;
}

public record ExtractedTable
{
    public required string TableName { get; init; }
    public required int PageNumber { get; init; }
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
    public float Confidence { get; init; }
}

public record ExtractedPage(int PageNumber, int Width, int Height, string Unit);
public record BoundingBox(float X, float Y, float Width, float Height, int Page);

public enum ExtractionStatus { Succeeded, PartialResults, Failed, PendingReview }
```

---

## Configuration (C#)

```csharp
public class DocumentExtractionOptions
{
    public DocumentExtractionProvider Provider { get; set; } = DocumentExtractionProvider.AzureDocumentIntelligence;

    // Azure AI Document Intelligence
    public string? AzureEndpoint { get; set; }
    public string? AzureApiKey { get; set; }           // or use Managed Identity
    public bool UseAzureManagedIdentity { get; set; } = true;

    // Model routing (document type → model ID)
    public IDictionary<string, string> ModelMap { get; set; } = new Dictionary<string, string>
    {
        ["ACORD125"]       = "prebuilt-document",
        ["LossRun"]        = "prebuilt-layout",
        ["Financial"]      = "prebuilt-layout",
        ["ApplicationForm"] = "prebuilt-document",
        ["default"]        = "prebuilt-document"
    };

    // Confidence thresholds
    public float LowConfidenceThreshold { get; set; } = 0.75f;
    public float AutoAcceptThreshold { get; set; } = 0.90f;

    // Retry
    public int MaxPollingAttempts { get; set; } = 30;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(3);
    public int MaxRetryAttempts { get; set; } = 3;
}

public enum DocumentExtractionProvider
{
    AzureDocumentIntelligence,
    AwsTextract,
    Tesseract,
    Mock
}
```

---

## Python Implementation (IDP Azure Function)

```python
# ksquare/document_extraction/providers/azure_extractor.py

from azure.ai.documentintelligence import DocumentIntelligenceClient
from azure.ai.documentintelligence.models import AnalyzeDocumentRequest
from azure.identity import DefaultAzureCredential
import asyncio

class AzureDocumentExtractor(IDocumentExtractor):

    def __init__(self, endpoint: str, use_managed_identity: bool = True):
        credential = DefaultAzureCredential() if use_managed_identity else AzureKeyCredential(api_key)
        self._client = DocumentIntelligenceClient(endpoint, credential)
        self._model_map = {
            "ACORD125":    "prebuilt-document",
            "LossRun":     "prebuilt-layout",
            "Financial":   "prebuilt-layout",
            "default":     "prebuilt-document"
        }

    async def extract_async(self, document: DocumentInput, model_hint=None, correlation_id=None):
        model_id = self._model_map.get(model_hint, self._model_map["default"])

        # Start long-running analysis operation
        poller = self._client.begin_analyze_document(
            model_id=model_id,
            body=AnalyzeDocumentRequest(url_source=str(document.document_uri))
            if document.document_uri else
            AnalyzeDocumentRequest(bytes_source=document.content)
        )

        # Poll until complete (Azure LRO pattern)
        result = poller.result()

        fields = []
        for doc in result.documents or []:
            for name, field in (doc.fields or {}).items():
                fields.append(ExtractedField(
                    name=name,
                    value=field.content,
                    confidence=field.confidence or 0.0,
                    page_number=field.bounding_regions[0].page_number if field.bounding_regions else None,
                    bounding_box=self._to_bbox(field.bounding_regions) if field.bounding_regions else None
                ))

        tables = []
        for i, table in enumerate(result.tables or []):
            headers = [cell.content for cell in table.cells if cell.row_index == 0]
            rows = self._extract_rows(table)
            tables.append(ExtractedTable(
                table_name=f"table_{i}",
                page_number=table.bounding_regions[0].page_number if table.bounding_regions else 1,
                headers=headers,
                rows=rows,
                confidence=0.9
            ))

        avg_confidence = sum(f.confidence for f in fields) / len(fields) if fields else 0.0

        return ExtractionResult(
            document_id=correlation_id or str(uuid.uuid4()),
            provider_operation_id=poller.id if hasattr(poller, 'id') else "unknown",
            status=ExtractionStatus.SUCCEEDED,
            fields=fields,
            tables=tables,
            pages=[ExtractedPage(p.page_number, p.width, p.height, p.unit) for p in result.pages],
            overall_confidence=avg_confidence,
            model_used=model_id,
            correlation_id=correlation_id
        )
```

---

## DI Registration (C# wrapper)

```csharp
builder.Services.AddKsDocumentExtraction(options =>
{
    builder.Configuration.GetSection("KSquare:DocumentExtraction").Bind(options);
    options.AzureApiKey = builder.Configuration["DocIntelligence--ApiKey"];
    options.UseAzureManagedIdentity = true;
});
```

---

## Extraction Confidence Routing

```
Field.Confidence >= AutoAcceptThreshold (0.90)  → Accept automatically
Field.Confidence >= LowConfidenceThreshold (0.75) → Accept with warning flag
Field.Confidence < LowConfidenceThreshold (0.75)  → Flag for human review
                                                    → Set ExtractionStatus = PendingReview

Overall: if any field < LowConfidenceThreshold → ExtractionStatus = PendingReview
         if all fields < LowConfidenceThreshold AND document structure unrecognized → Status = Failed
```

---

## Claude Code Build Prompt

```
Build a Python package called ksquare-document-extraction at path: shared/ksquare-document-extraction/

This package wraps Azure AI Document Intelligence for async document field extraction.
It is used inside Azure Functions (Python 3.11, async).

Package structure:
  shared/ksquare-document-extraction/
  ├── pyproject.toml
  ├── ksquare/
  │   └── document_extraction/
  │       ├── __init__.py
  │       ├── contracts.py           ← IDocumentExtractor ABC
  │       ├── models.py              ← DocumentInput, ExtractionResult, ExtractedField, ExtractedTable, etc.
  │       ├── config.py              ← ExtractionConfig dataclass
  │       ├── providers/
  │       │   ├── __init__.py
  │       │   ├── azure_extractor.py  ← AzureDocumentExtractor using azure-ai-documentintelligence
  │       │   └── mock_extractor.py   ← returns fixture ExtractionResult for tests
  │       └── routing.py             ← model_hint → Azure model ID routing logic
  └── tests/
      ├── test_azure_extractor.py
      ├── test_routing.py
      └── fixtures/
          └── sample_acord125.json    ← fixture ExtractionResult JSON for mock provider

AzureDocumentExtractor:
  - Use azure-ai-documentintelligence SDK (async client: DocumentIntelligenceClient)
  - Authenticate with DefaultAzureCredential if use_managed_identity=True, else AzureKeyCredential
  - begin_analyze_document with model_id from routing table
  - Wait for poller.result() (synchronous wait inside async function using run_in_executor or native async SDK)
  - Map DocumentAnalysisResult.documents[].fields → List[ExtractedField]
  - Map DocumentAnalysisResult.tables → List[ExtractedTable]
  - Compute overall_confidence as average of all field confidences
  - Set status to PENDING_REVIEW if any field confidence < 0.75
  - Preserve bounding_regions as BoundingBox objects

MockDocumentExtractor:
  - Load sample_acord125.json fixture
  - Return ExtractionResult from fixture, optionally override correlation_id
  - Use for unit tests and local dev without Azure credentials

pyproject.toml dependencies:
  azure-ai-documentintelligence>=1.0
  azure-identity>=1.15
  pydantic>=2.0

Tests:
  - MockExtractor returns ExtractionResult with expected fields
  - Low confidence field sets status to PENDING_REVIEW
  - routing.py returns correct model_id for each document type hint
  - ExtractedField.needs_review is True when confidence < 0.75
  Use pytest + pytest-asyncio.

Also build a .NET 8 C# thin wrapper library at path: shared/KSquare.DocumentExtraction/
This C# library calls the Python extraction function via HTTP (Azure Function URL) rather than
calling Azure Document Intelligence directly from .NET.

C# project structure:
  shared/KSquare.DocumentExtraction/
  ├── KSquare.DocumentExtraction.csproj
  ├── Contracts/
  │   └── IDocumentExtractor.cs
  ├── Models/
  │   ├── DocumentInput.cs
  │   ├── ExtractionResult.cs
  │   ├── ExtractedField.cs
  │   ├── ExtractedTable.cs
  │   └── ExtractionStatus.cs (enum)
  ├── Configuration/
  │   └── DocumentExtractionOptions.cs
  ├── Providers/
  │   ├── FunctionHttpDocumentExtractor.cs  ← calls ue-uw-idp-function HTTP trigger
  │   └── MockDocumentExtractor.cs
  └── Extensions/
      └── ServiceCollectionExtensions.cs

FunctionHttpDocumentExtractor:
  - POST to IDP Function URL: {FunctionBaseUrl}/api/extract
  - Body: { blobPath, contentType, modelHint, correlationId }
  - Returns ExtractionResult deserialized from JSON response
  - Polly retry: 3 attempts, exponential backoff on HttpRequestException / 5xx

ServiceCollectionExtensions.AddKsDocumentExtraction:
  - Registers IDocumentExtractor as scoped
  - Uses FunctionHttpDocumentExtractor (HTTP) by default
  - Uses MockDocumentExtractor when Provider = Mock

NuGet: Microsoft.Extensions.Http, Polly 8.x, System.Text.Json
```
