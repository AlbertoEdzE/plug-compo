# Component 24 — Document Narrative Engine

**Library**: `KSquare.DocumentNarrative`  
**Layer**: AI / Underwriting Intelligence  
**Default Provider**: Azure OpenAI (GPT-4o)  
**Alternate Providers**: Azure OpenAI GPT-4o-mini (cost mode), Mock  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Depends On**: Component 16 (RiskAnalysis), Component 13 (AgentOrchestrator — shares RAG index)

---

## Why This Is a Pluggable Component

The Submission Details and Underwriting Review screens display structured data — risk indicators,
loss run tables, coverage summaries. But underwriters also need **plain-English narratives** for:

1. **Risk Summary** — A 3–4 sentence executive summary of the risk profile for the submission header
2. **Loss Run Narrative** — Human-readable interpretation of loss trends ("Claims frequency has been
   declining over the past 3 years; however, a single large GL claim in 2022 inflated the 5-year average")
3. **Referral Recommendation Memo** — Structured justification when referring a submission to senior UW:
   risk factors, appetite assessment, recommended action with reasoning
4. **Underwriter File Note Draft** — A pre-drafted file note the underwriter can edit before saving:
   includes submission overview, key risk factors, coverage structure, and any special conditions

Without this component:
- Underwriters manually write these narratives from scratch — time-consuming
- Narrative quality is inconsistent across UWs
- No structured starting point for referral memos

`KSquare.DocumentNarrative` is pluggable because:
- The narrative style/tone can vary by customer (formal vs. conversational)
- The model can be upgraded or swapped
- A customer may want a smaller model for cost control (GPT-4o-mini mode)
- The output format (structured sections vs. free prose) can be configured

---

## Interface Contract

### Python (Azure Function — `ksquare-document-narrative`)

```python
from dataclasses import dataclass, field
from typing import Optional
from abc import ABC, abstractmethod
from enum import Enum

class NarrativeType(str, Enum):
    RISK_SUMMARY           = "RiskSummary"
    LOSS_RUN_NARRATIVE     = "LossRunNarrative"
    REFERRAL_MEMO          = "ReferralMemo"
    UNDERWRITER_FILE_NOTE  = "UnderwriterFileNote"

@dataclass
class SubmissionContext:
    submission_id: str
    institution_name: str
    institution_type: str                  # "K-12 Public District"
    state: str
    naics_code: str
    total_insured_value: float
    enrollment: int
    fte_employees: int
    effective_date: str                    # "2026-09-01"
    expiration_date: str
    coverage_lines: list[dict]             # [{"product": "GL", "limit": 5000000, "premium": 42000}]
    risk_indicators: dict                  # from RiskAnalysis Component 16
    appetite_fit_score: float
    appetite_classification: str           # "In Appetite" | "Borderline" | "Out of Appetite"

@dataclass
class LossHistoryContext:
    five_year_avg_loss_ratio: float
    largest_single_loss: float
    total_claims_count: int
    loss_trend: str                        # "Improving" | "Stable" | "Deteriorating"
    loss_run_years: list[dict]             # [{"year": 2023, "incurred": 85000, "claims": 3}]

@dataclass
class NarrativeRequest:
    submission_id: str
    narrative_type: NarrativeType
    submission_context: SubmissionContext
    loss_history: Optional[LossHistoryContext] = None
    underwriter_name: Optional[str] = None
    additional_notes: Optional[str] = None  # UW-supplied notes to incorporate
    correlation_id: Optional[str] = None

@dataclass
class NarrativeResult:
    submission_id: str
    narrative_type: NarrativeType
    narrative_text: str            # the generated narrative; ready to display or edit
    sections: dict[str, str]       # keyed sections for ReferralMemo and FileNote types
    word_count: int
    model_version: str
    prompt_version: str
    latency_ms: int
    correlation_id: Optional[str] = None

class DocumentNarrativeAdapter(ABC):
    @abstractmethod
    async def generate_narrative_async(self, request: NarrativeRequest) -> NarrativeResult:
        ...
```

### C# Wrapper

```csharp
namespace KSquare.DocumentNarrative.Contracts;

public interface IDocumentNarrativeAdapter
{
    Task<NarrativeResult> GenerateNarrativeAsync(
        NarrativeRequest request,
        CancellationToken ct = default);
}
```

---

## DI Registration

```csharp
// Program.cs in submission-api or underwriting-api
builder.Services.AddHttpClient<IDocumentNarrativeAdapter, DocumentNarrativeHttpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["KSquare:DocumentNarrative:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("x-functions-key", builder.Configuration["DocumentNarrative--FunctionKey"]);
});
```

---

## Prompt Design – Risk Summary

```python
RISK_SUMMARY_SYSTEM = """
You are an experienced commercial insurance underwriter specializing in education risk.
Write concise, professional risk summaries for use in underwriting files.

Requirements:
- 3-4 sentences maximum
- Focus on: institution profile, key risk indicators, appetite fit
- Use active voice; avoid passive constructions
- Do not use marketing language
- Do not recommend acceptance or decline (that is the underwriter's decision)
- Do not repeat the institution name more than once
"""

RISK_SUMMARY_USER = """
Submission: {institution_name} | {institution_type} | {state}
TIV: ${total_insured_value:,.0f} | Enrollment: {enrollment:,} | FTEs: {fte_employees:,}
Appetite Fit: {appetite_classification} ({appetite_fit_score:.0%})

Risk Indicators:
{risk_indicators_formatted}

Coverage Requested:
{coverage_lines_formatted}

Write a 3-4 sentence risk summary.
"""
```

---

## Prompt Design — Loss Run Narrative

```python
LOSS_RUN_SYSTEM = """
You are an experienced commercial insurance underwriter.
Write a factual, analytical narrative interpreting a loss history for an education risk submission.

Requirements:
- 3-5 sentences
- Report facts from the data; do not editorialize
- Note loss trend direction with supporting numbers
- Flag any unusual claims (frequency spike, large single loss > 10% of TIV)
- Do not conclude with a recommendation
"""

LOSS_RUN_USER = """
Institution: {institution_name} | {institution_type} | {state}
TIV: ${total_insured_value:,.0f}

Loss History Summary:
- 5-Year Average Loss Ratio: {five_year_avg_loss_ratio:.1%}
- Largest Single Loss: ${largest_single_loss:,.0f}
- Total Claims (5 years): {total_claims_count}
- Trend: {loss_trend}

Year-by-Year:
{loss_run_table}

Write a 3-5 sentence loss run narrative.
"""
```

---

## Prompt Design — Referral Recommendation Memo

```python
REFERRAL_MEMO_SYSTEM = """
You are an experienced commercial insurance underwriter preparing a referral memo for a senior underwriter.
Write a structured, factual memo presenting the submission for senior review.

The memo must have exactly these sections:
1. SUBMISSION OVERVIEW (2-3 sentences)
2. KEY RISK FACTORS (bullet list, 3-5 items)
3. LOSS HISTORY SUMMARY (2-3 sentences)
4. APPETITE ASSESSMENT (1-2 sentences based on appetite score)
5. REFERRAL REASON (1-2 sentences explaining why this requires senior review)
6. RECOMMENDED ACTION (one of: Approve / Decline / Request Additional Information / Refer to Reinsurance)

Be factual and concise. Do not use hedging language.
"""
```

---

## Prompt Design — Underwriter File Note

```python
FILE_NOTE_SYSTEM = """
You are an experienced commercial insurance underwriter drafting a file note.
This note will be stored in the underwriting file as a record of the underwriting decision process.

Structure:
1. SUBMISSION: institution name, type, state, effective date
2. COVERAGE STRUCTURE: lines, limits, retentions, total premium
3. RISK ASSESSMENT: key factors (positive and negative)
4. LOSS EXPERIENCE: trend and notable claims
5. SPECIAL CONDITIONS: any endorsements, exclusions, or conditions to note
6. UNDERWRITER NOTES: incorporate any additional notes provided

Be precise and professional. Use past tense where appropriate ("Review of the submission revealed...").
"""
```

---

## Implementation

```python
class AzureOpenAiNarrativeAdapter(DocumentNarrativeAdapter):

    PROMPTS = {
        NarrativeType.RISK_SUMMARY:          (RISK_SUMMARY_SYSTEM,  RISK_SUMMARY_USER),
        NarrativeType.LOSS_RUN_NARRATIVE:    (LOSS_RUN_SYSTEM,      LOSS_RUN_USER),
        NarrativeType.REFERRAL_MEMO:         (REFERRAL_MEMO_SYSTEM, REFERRAL_MEMO_USER),
        NarrativeType.UNDERWRITER_FILE_NOTE: (FILE_NOTE_SYSTEM,     FILE_NOTE_USER),
    }

    async def generate_narrative_async(self, request: NarrativeRequest) -> NarrativeResult:
        start = time.monotonic()
        system_prompt, user_template = self.PROMPTS[request.narrative_type]

        # Format data for prompt
        user_message = self._build_user_message(user_template, request)

        response = await self._client.chat.completions.create(
            model=self._options.deployment_name,
            messages=[
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_message}
            ],
            temperature=0.3,   # slight variation for natural prose; not fully deterministic
            max_tokens=self._max_tokens_for(request.narrative_type)
        )

        latency_ms = int((time.monotonic() - start) * 1000)
        narrative_text = response.choices[0].message.content.strip()
        sections = self._parse_sections(narrative_text, request.narrative_type)

        self._tracer.record(
            operation=f"narrative_{request.narrative_type.value.lower()}",
            model=self._options.deployment_name,
            prompt_tokens=response.usage.prompt_tokens,
            completion_tokens=response.usage.completion_tokens,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id
        )

        return NarrativeResult(
            submission_id=request.submission_id,
            narrative_type=request.narrative_type,
            narrative_text=narrative_text,
            sections=sections,
            word_count=len(narrative_text.split()),
            model_version=response.model,
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id
        )

    def _max_tokens_for(self, narrative_type: NarrativeType) -> int:
        return {
            NarrativeType.RISK_SUMMARY:          200,
            NarrativeType.LOSS_RUN_NARRATIVE:    250,
            NarrativeType.REFERRAL_MEMO:         600,
            NarrativeType.UNDERWRITER_FILE_NOTE: 800,
        }[narrative_type]

    def _parse_sections(self, text: str, narrative_type: NarrativeType) -> dict[str, str]:
        # For memo and file note types: split on numbered section headers
        if narrative_type in (NarrativeType.REFERRAL_MEMO, NarrativeType.UNDERWRITER_FILE_NOTE):
            import re
            sections = {}
            parts = re.split(r'\n\d+\.\s+([A-Z\s]+):\n', text)
            # parts alternates between content and header name
            for i in range(1, len(parts) - 1, 2):
                sections[parts[i].strip()] = parts[i + 1].strip()
            return sections
        return {"full": text}
```

---

## Azure Function Entry Points

```python
@app.function_name("GenerateNarrative")
@app.route(route="narrative/generate", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def generate_narrative(req: func.HttpRequest) -> func.HttpResponse:
    body = req.get_json()
    request = NarrativeRequest(
        submission_id=body["submission_id"],
        narrative_type=NarrativeType(body["narrative_type"]),
        submission_context=SubmissionContext(**body["submission_context"]),
        loss_history=LossHistoryContext(**body["loss_history"]) if body.get("loss_history") else None,
        underwriter_name=body.get("underwriter_name"),
        additional_notes=body.get("additional_notes"),
        correlation_id=body.get("correlation_id")
    )
    adapter = _get_adapter()
    result = await adapter.generate_narrative_async(request)
    return func.HttpResponse(json.dumps(asdict(result)), mimetype="application/json")
```

---

## Configuration

```python
@dataclass
class DocumentNarrativeOptions:
    provider: str = "AzureOpenAi"      # "AzureOpenAi" | "Mock"
    azure_openai_endpoint: str = ""
    deployment_name: str = "gpt-4o"
    prompt_version: str = "v1"
    temperature: float = 0.3
```

---

## Integration with Submission Details Screen

```
Component 16 (RiskAnalysis) completes → RiskAnalysisResult available
                    ↓
submission-api calls IDocumentNarrativeAdapter:
  - GenerateNarrativeAsync(NarrativeType.RiskSummary, submissionContext)
  - GenerateNarrativeAsync(NarrativeType.LossRunNarrative, submissionContext, lossHistory)
  - Results cached in submission record
                    ↓
Submission Details screen displays:
  - "Risk Summary" section: narrative_text from RiskSummary
  - "Loss Experience" section: narrative_text from LossRunNarrative
  - Underwriter can click "Generate Referral Memo" button → triggers ReferralMemo type
  - Underwriter can click "Draft File Note" button → triggers UnderwriterFileNote type
  - All generated narratives are editable before saving to the underwriting file
```

---

## Evaluation Metrics

| Metric | Target | Alert |
|---|---|---|
| Human acceptance rate (edit ≤ 20% of words) | ≥ 75% of generated narratives | < 55% |
| Factual accuracy (no hallucinated numbers) | 100% (zero tolerance) | Any hallucination |
| P95 latency (RiskSummary) | ≤ 3,000 ms | > 6,000 ms |
| P95 latency (ReferralMemo) | ≤ 5,000 ms | > 10,000 ms |
| Daily cost (GPT-4o, ~150 narratives) | < $15/day | > $40/day |

Factual accuracy is validated by the offline eval pipeline (Component 17): generated numbers are
cross-checked against the structured input data to detect hallucinated values.

---

## Failure States

| Scenario | Behaviour |
|---|---|
| LLM API unavailable | Return NarrativeResult with narrative_text = "" and log error; caller falls back to blank narrative |
| LLM generates text with hallucinated numbers | Detected by eval pipeline (post-hoc); alert ops; flag narrative for human review |
| Input data missing (no loss history) | Generate narrative without loss history section; do not fabricate loss data |
| Narrative exceeds max_tokens | GPT truncates naturally; acceptable for this use case — no retry needed |

---

## Claude Code Build Prompt

```
Build a Python 3.11 Azure Function package called ksquare-document-narrative at path:
shared-python/ksquare-document-narrative/

This package generates four types of plain-English narratives for underwriting use:
RiskSummary, LossRunNarrative, ReferralMemo, and UnderwriterFileNote.
Each narrative is generated from structured submission and risk analysis data using Azure OpenAI GPT-4o.

Package structure:
  shared-python/ksquare-document-narrative/
  ├── function_app.py                 ← Azure Function: POST /narrative/generate
  ├── contracts.py                    ← NarrativeType enum, SubmissionContext, LossHistoryContext,
  │                                      NarrativeRequest, NarrativeResult
  ├── options.py                      ← DocumentNarrativeOptions dataclass
  ├── prompts.py                      ← System + user prompt constants for all 4 narrative types
  ├── providers/
  │   ├── azure_openai_narrative.py   ← AzureOpenAiNarrativeAdapter; routes to correct prompt pair
  │   └── mock_narrative.py           ← MockNarrativeAdapter; returns deterministic placeholder text
  ├── factory.py
  ├── requirements.txt
  └── tests/
      ├── test_azure_openai_narrative.py
      └── test_mock_narrative.py

AzureOpenAiNarrativeAdapter.generate_narrative_async:
  - Select system + user prompt pair based on narrative_type
  - Format user message by injecting structured data into user template
  - Call Azure OpenAI with temperature=0.3, appropriate max_tokens per narrative type
  - For ReferralMemo and UnderwriterFileNote: parse numbered section headers into sections dict
  - Record token usage and latency via LlmTracer
  - On API error: return NarrativeResult with empty narrative_text; do not throw

MockNarrativeAdapter:
  - Return fixed placeholder text per narrative_type:
    RiskSummary: "Mock risk summary for {institution_name}. Appetite fit: {appetite_classification}."
    LossRunNarrative: "Mock loss run narrative. 5-year average loss ratio: {five_year_avg_loss_ratio:.1%}."
    ReferralMemo: Multi-section placeholder with each section header filled with "Mock content."
    UnderwriterFileNote: Multi-section placeholder

Tests:
  - generate_narrative_async for RiskSummary returns non-empty narrative_text
  - generate_narrative_async for ReferralMemo returns sections dict with at least 4 keys
  - LlmTracer records correct operation name per narrative_type
  - API error returns NarrativeResult with empty narrative_text (no exception thrown)
  - Mock returns narrative_text containing institution_name from context
  Use pytest + respx + pytest-asyncio.

Requirements:
  openai>=1.30.0
  azure-identity>=1.16.0
  azure-functions>=1.19.0
  pytest
  pytest-asyncio
  respx
```
