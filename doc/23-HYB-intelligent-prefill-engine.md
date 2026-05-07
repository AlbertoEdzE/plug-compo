# Component 23 — Intelligent Prefill Engine

**Library**: `KSquare.IntelligentPrefill`  
**Layer**: AI / Document Intelligence  
**Default Provider**: Azure OpenAI (GPT-4o — higher accuracy for structured extraction tasks)  
**Alternate Providers**: Azure OpenAI GPT-4o-mini (cost mode), Mock  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Depends On**: Component 10 (DocumentExtraction), Component 12 (ExtractionMapper)

---

## Why This Is a Pluggable Component

Component 12 (`KSquare.ExtractionMapper`) maps extracted document fields to typed domain models
using **deterministic YAML rules**. Rule-based mapping works well for structured forms (ACORD 125)
but fails when:

- Field labels vary ("No. of Students" vs "Current Enrollment" vs "Student Count")
- Values appear in free-text narrative blocks rather than labeled fields
- The OCR returns partial or misspelled field names
- New document variants appear that haven't been coded into YAML rules yet

`KSquare.IntelligentPrefill` is the **LLM fallback layer** that runs after the rule-based mapper:
1. Takes the full document text + the set of fields that the rule-mapper **could NOT fill**
2. Uses an LLM to reason about the document text and attempt to fill those unmapped fields
3. Returns each filled value with a confidence score and the source text fragment used
4. Human review is triggered for any value below the confidence threshold

This is separate from Component 13 (AgentOrchestrator) because:
- This is a **batch inference job** triggered by a document event — not a conversation
- It runs on every new document upload, not on demand from a user
- It is scoped to field extraction, not open-ended Q&A
- It has its own prompt, its own evaluation metrics, and its own latency/cost profile

---

## Interface Contract

### Python (Azure Function — `ksquare-intelligent-prefill`)

```python
from dataclasses import dataclass, field
from typing import Optional
from abc import ABC, abstractmethod

@dataclass
class UnmappedField:
    canonical_field: str       # e.g., "total_enrollment", "naics_code", "effective_date"
    display_label: str         # human-readable label for prompt context
    expected_type: str         # "string" | "integer" | "decimal" | "date" | "boolean"
    description: str           # "Total number of enrolled students across all grades"

@dataclass
class PrefillFieldResult:
    canonical_field: str
    value: Optional[str]       # extracted value as string; caller casts to expected_type
    confidence: float          # 0.0 – 1.0
    source_text: str           # verbatim document fragment used to derive the value
    reasoning: str             # LLM's brief explanation (for review UI display)
    needs_review: bool         # confidence < options.review_threshold

@dataclass
class PrefillRequest:
    document_id: str
    document_text: str         # full plain-text content of the document
    document_type: str         # "ApplicationForm" | "FinancialStatement" | "LossRun" | "Supporting"
    unmapped_fields: list[UnmappedField]
    correlation_id: Optional[str] = None

@dataclass
class PrefillResult:
    document_id: str
    field_results: list[PrefillFieldResult]
    total_fields_requested: int
    total_fields_filled: int   # confidence >= 0.50
    total_needs_review: int    # confidence < review_threshold
    model_version: str
    prompt_version: str
    latency_ms: int
    correlation_id: Optional[str] = None

    @property
    def fill_rate(self) -> float:
        return self.total_fields_filled / max(self.total_fields_requested, 1)

class IntelligentPrefillAdapter(ABC):
    @abstractmethod
    async def prefill_async(self, request: PrefillRequest) -> PrefillResult:
        ...
```

### C# Wrapper

```csharp
namespace KSquare.IntelligentPrefill.Contracts;

public interface IIntelligentPrefillAdapter
{
    Task<PrefillResult> PrefillAsync(
        PrefillRequest request,
        CancellationToken ct = default);
}
```

---

## DI Registration

```csharp
// Program.cs in submission-api or document-processing service
builder.Services.AddHttpClient<IIntelligentPrefillAdapter, IntelligentPrefillHttpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["KSquare:IntelligentPrefill:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("x-functions-key", builder.Configuration["IntelligentPrefill--FunctionKey"]);
});
```

---

## Prompt Design

The prompt uses a **batched extraction pattern** — all unmapped fields are sent in a single LLM call
to minimize cost and latency, rather than one call per field.

```python
PREFILL_SYSTEM_PROMPT = """
You are a document field extraction assistant for an education insurance underwriting system.

You will receive:
1. A document (application form, financial statement, or loss run) as plain text
2. A list of fields that could not be automatically extracted using rule-based methods

Your job: attempt to find the value for each field in the document text.

Rules:
- Only extract values explicitly present in the document — do not infer or calculate
- If a value is not found, set value to null and confidence to 0.0
- Confidence 0.9-1.0: value is clearly labeled and unambiguous
- Confidence 0.7-0.8: value is present but requires interpretation
- Confidence 0.5-0.6: value may be present but is ambiguous
- Confidence 0.0-0.4: not found or very uncertain
- source_text must be a verbatim fragment from the document (max 100 chars)
- reasoning must be one sentence explaining your extraction decision

Respond ONLY with valid JSON. No other text.
"""

PREFILL_USER_TEMPLATE = """
Document type: {document_type}

---DOCUMENT TEXT (first {max_chars} chars)---
{document_text}
---END DOCUMENT---

Fields to extract:
{fields_json}

Respond with JSON:
{{
  "results": [
    {{
      "canonical_field": "<field_name>",
      "value": "<extracted value or null>",
      "confidence": <0.0-1.0>,
      "source_text": "<verbatim fragment>",
      "reasoning": "<one sentence>"
    }},
    ...
  ]
}}
"""
```

---

## Implementation

```python
class AzureOpenAiPrefillAdapter(IntelligentPrefillAdapter):

    # Chunk fields into batches to stay within context limits
    FIELDS_PER_BATCH = 15

    async def prefill_async(self, request: PrefillRequest) -> PrefillResult:
        start = time.monotonic()
        all_results: list[PrefillFieldResult] = []

        # Truncate document to manageable size; keep beginning where key fields usually appear
        doc_text = request.document_text[:self._options.max_document_chars]

        # Batch fields to avoid token overrun
        batches = [
            request.unmapped_fields[i:i + self.FIELDS_PER_BATCH]
            for i in range(0, len(request.unmapped_fields), self.FIELDS_PER_BATCH)
        ]

        for batch in batches:
            batch_results = await self._extract_batch(doc_text, request.document_type, batch)
            all_results.extend(batch_results)

        latency_ms = int((time.monotonic() - start) * 1000)
        threshold = self._options.review_confidence_threshold

        for result in all_results:
            result.needs_review = result.confidence < threshold

        filled = sum(1 for r in all_results if r.confidence >= 0.50)
        needs_review = sum(1 for r in all_results if r.needs_review)

        return PrefillResult(
            document_id=request.document_id,
            field_results=all_results,
            total_fields_requested=len(request.unmapped_fields),
            total_fields_filled=filled,
            total_needs_review=needs_review,
            model_version=self._options.deployment_name,
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id
        )

    async def _extract_batch(
        self,
        doc_text: str,
        doc_type: str,
        fields: list[UnmappedField]
    ) -> list[PrefillFieldResult]:
        fields_json = json.dumps([
            {
                "canonical_field": f.canonical_field,
                "display_label": f.display_label,
                "expected_type": f.expected_type,
                "description": f.description
            }
            for f in fields
        ], indent=2)

        messages = [
            {"role": "system", "content": PREFILL_SYSTEM_PROMPT},
            {"role": "user", "content": PREFILL_USER_TEMPLATE.format(
                document_type=doc_type,
                max_chars=self._options.max_document_chars,
                document_text=doc_text,
                fields_json=fields_json
            )}
        ]

        response = await self._client.chat.completions.create(
            model=self._options.deployment_name,
            messages=messages,
            temperature=0.0,
            max_tokens=1500,
            response_format={"type": "json_object"}
        )

        data = json.loads(response.choices[0].message.content)
        return [PrefillFieldResult(**r) for r in data["results"]]
```

---

## Azure Function Entry Point

```python
import azure.functions as func

app = func.FunctionApp()

@app.function_name("IntelligentPrefill")
@app.route(route="prefill/run", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def intelligent_prefill(req: func.HttpRequest) -> func.HttpResponse:
    body = req.get_json()
    request = PrefillRequest(
        document_id=body["document_id"],
        document_text=body["document_text"],
        document_type=body["document_type"],
        unmapped_fields=[UnmappedField(**f) for f in body.get("unmapped_fields", [])],
        correlation_id=body.get("correlation_id")
    )
    adapter = _get_adapter()
    result = await adapter.prefill_async(request)
    return func.HttpResponse(json.dumps(asdict(result)), mimetype="application/json")
```

---

## Configuration

```python
@dataclass
class IntelligentPrefillOptions:
    provider: str = "AzureOpenAi"       # "AzureOpenAi" | "Mock"
    azure_openai_endpoint: str = ""
    deployment_name: str = "gpt-4o"     # higher-accuracy model for extraction
    prompt_version: str = "v1"
    max_document_chars: int = 8000      # ~6,000 tokens for most application forms
    review_confidence_threshold: float = 0.75
    fields_per_batch: int = 15
```

---

## Integration with ExtractionMapper (Component 12)

```
Component 10: DocumentExtraction → ExtractedFieldSet (raw KV pairs from OCR)
                    ↓
Component 12: ExtractionMapper → MappingResult<T>
              - Maps fields it CAN map via YAML rules
              - Produces list of unmapped required fields
                    ↓
Component 23: IntelligentPrefill (this component)
              - Receives: document_text + unmapped_fields list
              - Attempts LLM extraction for each unmapped field
              - Returns: PrefillResult with confidence per field
                    ↓
submission-api: Merge mappingResult + prefillResult
              - Fields with confidence >= 0.75 → auto-prefill in form
              - Fields with confidence 0.50-0.74 → prefill with yellow highlight (needs review)
              - Fields with confidence < 0.50 → leave blank, flag for manual entry
```

---

## Review UI Integration

The prefill results feed directly into the New Submission screen's review state:

```json
{
  "canonical_field": "total_enrollment",
  "value": "4250",
  "confidence": 0.82,
  "source_text": "Total student enrollment: 4,250",
  "reasoning": "Found labeled field 'Total student enrollment' with clear numeric value.",
  "needs_review": false
}
```

The frontend uses `needs_review` and `source_text` to render:
- Green checkmark on auto-filled fields (confidence ≥ 0.75)
- Yellow caution icon with source text tooltip (0.50 – 0.74)
- Empty field with red asterisk (< 0.50)

---

## Evaluation Metrics

| Metric | Target | Alert |
|---|---|---|
| Field fill rate (confidence ≥ 0.50) | ≥ 80% of unmapped fields | < 60% |
| High-confidence fill rate (≥ 0.75) | ≥ 60% of unmapped fields | < 40% |
| False positive rate (wrong value, high confidence) | ≤ 5% | > 10% |
| P95 latency per document | ≤ 5,000 ms | > 10,000 ms |
| Daily cost (GPT-4o, ~200 docs) | < $20/day | > $50/day |

Offline evaluation runs weekly via Component 17 (LlmObservability) against a labeled ground-truth dataset of 50 application forms with known field values.

---

## Failure States

| Scenario | Behaviour |
|---|---|
| LLM returns malformed JSON | Retry once; on second failure return all fields with value=null, confidence=0.0 |
| Document text empty | Return empty PrefillResult immediately — no LLM call |
| No unmapped fields | Return empty PrefillResult immediately — no LLM call |
| Token budget exceeded (very long document) | Truncate to max_document_chars; log warning with document_id |
| LLM API unavailable | Return all fields with confidence=0.0; submission still proceeds with blanks |

---

## Claude Code Build Prompt

```
Build a Python 3.11 Azure Function package called ksquare-intelligent-prefill at path:
shared-python/ksquare-intelligent-prefill/

This package fills form fields that the rule-based ExtractionMapper (Component 12) could not map.
It sends the document text plus a list of unmapped fields to Azure OpenAI GPT-4o and returns
each field's extracted value with a confidence score and source text fragment.

Package structure:
  shared-python/ksquare-intelligent-prefill/
  ├── function_app.py              ← Azure Function HTTP trigger
  ├── contracts.py                 ← UnmappedField, PrefillRequest, PrefillFieldResult, PrefillResult
  ├── options.py                   ← IntelligentPrefillOptions dataclass
  ├── prompts.py                   ← PREFILL_SYSTEM_PROMPT + PREFILL_USER_TEMPLATE
  ├── providers/
  │   ├── azure_openai_prefill.py  ← AzureOpenAiPrefillAdapter; batch loop; temperature=0.0
  │   └── mock_prefill.py          ← MockPrefillAdapter; returns confidence=0.80 for all fields with "MOCK_VALUE"
  ├── factory.py
  ├── requirements.txt
  └── tests/
      ├── test_azure_openai_prefill.py
      └── test_mock_prefill.py

AzureOpenAiPrefillAdapter:
  - Batch unmapped_fields into groups of FIELDS_PER_BATCH (default 15) to avoid token overrun
  - For each batch: single LLM call with temperature=0.0, response_format json_object
  - Merge batch results
  - Mark result.needs_review = True where confidence < review_confidence_threshold
  - On JSON parse error: return all fields in batch with value=null, confidence=0.0

MockPrefillAdapter:
  - For each unmapped field: return value="MOCK_VALUE", confidence=0.80, needs_review=False
  - Useful for testing the submission prefill flow without LLM costs

Tests:
  - prefill_async returns PrefillResult with correct total_fields_requested count
  - High confidence results have needs_review = False
  - Low confidence results (< threshold) have needs_review = True
  - Empty unmapped_fields list returns PrefillResult with empty field_results immediately
  - JSON parse error from LLM returns safe fallback (all null, confidence 0.0)
  - Batching: 20 unmapped fields triggers 2 LLM calls (batch size 15)
  Use pytest + respx + pytest-asyncio.

Requirements:
  openai>=1.30.0
  azure-identity>=1.16.0
  azure-functions>=1.19.0
  pytest
  pytest-asyncio
  respx
```
