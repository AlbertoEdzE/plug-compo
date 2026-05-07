# Component 11 — Document Classification Adapter

**Library**: `KSquare.DocumentClassification`  
**Layer**: Intelligence  
**Default Provider**: Azure AI Document Intelligence (custom classifier) + heuristic fallback  
**Alternate Providers**: OpenAI GPT-4o Vision, rule-based keyword classifier  
**Language**: Python 3.11 (primary — runs in IDP Function) + C# .NET 8 (HTTP wrapper)

---

## Why This Is a Pluggable Component

Before extraction can run (Component 10), the system must know what type of document it is dealing with:
- ACORD 125 (Commercial Lines Application)
- Loss Run (prior claims history)
- Financial Statements
- Supporting Documents (certificates, schedules, photos)
- Unknown / Needs Manual Review

Classification drives which extraction model to invoke, which validation rules to apply,
and which downstream queue/topic to route the document to.

Complexity justifying a library:
- Multiple classification strategies with different accuracy/cost tradeoffs
- Fallback chain: Azure classifier → heuristic (filename + first-page text) → "Unknown"
- Confidence threshold routing (below threshold = flag for manual assignment)
- Custom Azure classifier requires training data management
- Classification result feeds into ExtractionResult.DetectedDocumentType (Component 10)

---

## Interface Contract (Python)

```python
# ksquare/document_classification/contracts.py

from abc import ABC, abstractmethod
from typing import Optional

class IDocumentClassifier(ABC):
    @abstractmethod
    async def classify_async(
        self,
        document: "DocumentInput",
        correlation_id: Optional[str] = None
    ) -> "ClassificationResult":
        """Classify a document and return its type with confidence."""
        ...
```

---

## Interface Contract (C# wrapper)

```csharp
namespace KSquare.DocumentClassification.Contracts;

public interface IDocumentClassifier
{
    Task<ClassificationResult> ClassifyAsync(
        DocumentInput input,
        CancellationToken ct = default);
}
```

---

## Models (C#)

```csharp
namespace KSquare.DocumentClassification.Models;

public record ClassificationResult
{
    public required string DocumentType { get; init; }    // "ACORD125", "LossRun", "Financial", "Supporting", "Unknown"
    public required float Confidence { get; init; }       // 0.0 – 1.0
    public required ClassificationMethod Method { get; init; }
    public IReadOnlyList<ClassificationCandidate> AlternativeCandidates { get; init; } = [];
    public bool RequiresManualReview => Confidence < 0.70f || DocumentType == "Unknown";
    public string? CorrelationId { get; init; }
    public DateTimeOffset ClassifiedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record ClassificationCandidate(string DocumentType, float Confidence);

public enum ClassificationMethod
{
    AzureDocumentClassifier,    // custom trained Azure classifier
    HeuristicKeyword,           // filename + first-page text keyword matching
    GptVision,                  // GPT-4o with document page image
    Manual                      // human-assigned in review queue
}
```

---

## Document Type Taxonomy

```
DocumentType         Aliases / Keywords                              Extraction Model
─────────────────────────────────────────────────────────────────────────────────────
ACORD125             "acord 125", "commercial lines application"     prebuilt-document
ACORD126             "acord 126", "commercial general liability"     prebuilt-document  
LossRun              "loss run", "claims history", "prior losses"    prebuilt-layout
FinancialStatement   "balance sheet", "p&l", "profit and loss"      prebuilt-layout
PropertySchedule     "property schedule", "schedule of locations"   prebuilt-layout
Certificate          "certificate of insurance", "acord 25"         prebuilt-document
Supporting           everything else with low confidence            prebuilt-document
Unknown              classifier confidence < 0.50                   none (manual review)
```

---

## Configuration (Python + C#)

```python
# config.py
@dataclass
class ClassificationConfig:
    provider: str = "azure_then_heuristic"   # "azure_only", "heuristic_only", "gpt_vision"
    azure_endpoint: str = ""
    azure_classifier_model_id: str = "ksquare-doc-classifier-v1"
    use_managed_identity: bool = True
    confidence_threshold_auto: float = 0.85
    confidence_threshold_review: float = 0.70
    heuristic_fallback_enabled: bool = True
```

```csharp
public class DocumentClassificationOptions
{
    public DocumentClassificationProvider Provider { get; set; }
        = DocumentClassificationProvider.AzureThenHeuristic;
    public string? AzureEndpoint { get; set; }
    public string? AzureClassifierModelId { get; set; } = "ksquare-doc-classifier-v1";
    public bool UseAzureManagedIdentity { get; set; } = true;
    public float AutoAcceptThreshold { get; set; } = 0.85f;
    public float ReviewThreshold { get; set; } = 0.70f;
}

public enum DocumentClassificationProvider
{
    AzureThenHeuristic,
    AzureOnly,
    HeuristicOnly,
    GptVision,
    Mock
}
```

---

## Heuristic Classifier (Fallback)

```python
# providers/heuristic_classifier.py

KEYWORD_RULES = {
    "ACORD125":          ["acord 125", "commercial lines application", "acord125"],
    "ACORD126":          ["acord 126", "acord126", "commercial general liability"],
    "LossRun":           ["loss run", "claims history", "prior losses", "loss history"],
    "FinancialStatement": ["balance sheet", "profit and loss", "income statement", "p&l"],
    "PropertySchedule":  ["property schedule", "schedule of locations", "building schedule"],
    "Certificate":       ["certificate of insurance", "acord 25", "acord25"],
}

class HeuristicDocumentClassifier(IDocumentClassifier):
    async def classify_async(self, document: DocumentInput, correlation_id=None):
        # Combine filename + first 500 chars of extracted text if available
        text_signal = (document.file_name or "").lower()
        if document.first_page_text:
            text_signal += " " + document.first_page_text[:500].lower()

        best_type = "Unknown"
        best_score = 0.0

        for doc_type, keywords in KEYWORD_RULES.items():
            hits = sum(1 for kw in keywords if kw in text_signal)
            score = min(hits / len(keywords), 1.0) * 0.80   # cap heuristic at 0.80

            if score > best_score:
                best_score = score
                best_type = doc_type

        confidence = best_score if best_score > 0 else 0.0
        doc_type = best_type if confidence >= 0.40 else "Unknown"

        return ClassificationResult(
            document_type=doc_type,
            confidence=confidence,
            method=ClassificationMethod.HEURISTIC_KEYWORD,
            correlation_id=correlation_id
        )
```

---

## Classification + Extraction Integration

```python
# In the IDP Azure Function (orchestration step):

async def process_document(blob_path: str, correlation_id: str):
    doc_input = DocumentInput(blob_path=blob_path, content_type=detect_content_type(blob_path))

    # Step 1: Classify
    classification = await classifier.classify_async(doc_input, correlation_id)

    if classification.requires_manual_review:
        # Publish to human-review queue
        await publish_event("document.needs-review", {
            "blobPath": blob_path,
            "classificationResult": classification.to_dict(),
            "correlationId": correlation_id
        })
        return

    # Step 2: Extract using model hint from classification
    extraction = await extractor.extract_async(
        doc_input,
        model_hint=classification.document_type,
        correlation_id=correlation_id
    )

    # Step 3: Publish extraction result
    await publish_event("document.extracted", {
        "blobPath": blob_path,
        "documentType": classification.document_type,
        "extractionResult": extraction.to_dict(),
        "correlationId": correlation_id
    })
```

---

## Claude Code Build Prompt

```
Build a Python package called ksquare-document-classification at path: shared/ksquare-document-classification/

This package classifies documents into known types (ACORD125, LossRun, Financial, etc.)
using a fallback chain: Azure classifier → heuristic keyword matching → Unknown.

Package structure:
  shared/ksquare-document-classification/
  ├── pyproject.toml
  ├── ksquare/
  │   └── document_classification/
  │       ├── __init__.py
  │       ├── contracts.py            ← IDocumentClassifier ABC
  │       ├── models.py               ← ClassificationResult, ClassificationCandidate, ClassificationMethod enum
  │       ├── config.py               ← ClassificationConfig dataclass
  │       ├── providers/
  │       │   ├── __init__.py
  │       │   ├── azure_classifier.py     ← Azure AI Document Intelligence custom classifier
  │       │   ├── heuristic_classifier.py ← keyword matching fallback
  │       │   └── mock_classifier.py      ← returns configurable fixture result
  │       └── pipeline.py             ← AzureThenHeuristic composite classifier
  └── tests/
      ├── test_heuristic_classifier.py
      ├── test_pipeline.py
      └── fixtures/
          └── sample_classification_result.json

AzureDocumentClassifier:
  - Use DocumentIntelligenceClient.begin_classify_document(model_id, body=...)
  - Parse DocumentClassificationResult.documents[0].docType and confidence
  - Return ClassificationResult with method=AzureDocumentClassifier
  - On exception or low confidence: return None to let pipeline fall through to heuristic

HeuristicDocumentClassifier:
  - Implement KEYWORD_RULES dict as shown in spec
  - Score each type by keyword hit ratio
  - Cap confidence at 0.80 (heuristic is never perfect)
  - Set document_type = "Unknown" if best score < 0.40

AzureThenHeuristicPipeline (in pipeline.py):
  - Try AzureDocumentClassifier first
  - If result.confidence >= config.confidence_threshold_auto: return Azure result
  - Else: try HeuristicDocumentClassifier
  - If heuristic confidence > Azure confidence: return heuristic result
  - Else: return Azure result (even if low confidence, preserve method label)
  - If both fail (Unknown from both): return ClassificationResult(Unknown, 0.0, requires_manual_review=True)

MockDocumentClassifier:
  - Accepts a pre-configured ClassificationResult to return
  - Default: ACORD125 with confidence=0.92

pyproject.toml dependencies:
  azure-ai-documentintelligence>=1.0
  azure-identity>=1.15
  pydantic>=2.0

Tests:
  - HeuristicClassifier returns ACORD125 for text containing "acord 125"
  - HeuristicClassifier returns Unknown for text with no matching keywords
  - Pipeline uses Azure result when confidence >= threshold
  - Pipeline falls back to heuristic when Azure confidence below threshold
  - requires_manual_review is True when confidence < 0.70
  Use pytest + pytest-asyncio + pytest-mock.

Also build C# thin HTTP wrapper at shared/KSquare.DocumentClassification/:
  Same pattern as KSquare.DocumentExtraction wrapper:
  - IDocumentClassifier interface
  - ClassificationResult, ClassificationMethod models
  - FunctionHttpDocumentClassifier calls IDP Function HTTP endpoint
  - MockDocumentClassifier for tests
  - AddKsDocumentClassification(Action<DocumentClassificationOptions>) DI extension
```
