# Component 22 — AI Email Triage Adapter

**Library**: `KSquare.AiEmailTriage`  
**Layer**: Integration / AI / Communication  
**Default Provider**: Azure OpenAI (GPT-4o-mini — cost-efficient for structured extraction)  
**Alternate Providers**: Azure OpenAI GPT-4o, Mock  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Depends On**: Component 02 (EventBus) — publishes triage result as event

---

## Why This Is a Pluggable Component

When a submission email arrives (via Component 07 — EmailIngestion), the raw MIME email body is
unstructured prose. Component 07 parses MIME structure but does not understand content.

A separate AI triage step is needed to:
1. Classify intent — is this a new submission, a renewal, a broker info request, a complaint, or other?
2. Extract key entities — institution name, broker firm, state, coverage lines mentioned, effective date
3. Suggest routing — which underwriter queue or team should receive this?
4. Detect urgency signals — words like "urgent", "expiring", "deadline"

Without this component, the submission pipeline either:
- Routes all emails as "New Submission" and lets underwriters manually sort
- Hard-codes keyword rules that miss natural language variations

`KSquare.AiEmailTriage` is pluggable because:
- The LLM model can change (GPT-4o-mini → GPT-4o for higher accuracy)
- The extraction schema evolves as new product lines are added
- A customer may prefer rule-based triage instead of LLM for cost reasons (Mock provider does this)
- The prompt is versioned and can be A/B tested

---

## Interface Contract

### Python (Azure Function — `ksquare-ai-email-triage`)

```python
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Optional

@dataclass
class EmailTriageRequest:
    email_id: str
    subject: str
    body_text: str             # plain text body; HTML stripped by Component 07
    sender_email: str
    sender_name: Optional[str]
    received_at: str           # ISO 8601
    attachment_names: list[str] = field(default_factory=list)
    correlation_id: Optional[str] = None

@dataclass
class ExtractedEmailEntity:
    field_name: str            # "institution_name", "broker_firm", "state", etc.
    value: str
    confidence: float          # 0.0 – 1.0
    source_text: str           # verbatim text fragment LLM used to extract this

@dataclass
class EmailTriageResult:
    email_id: str
    intent: str                # "NewSubmission" | "Renewal" | "InfoRequest" | "Complaint" | "Other"
    intent_confidence: float
    extracted_entities: list[ExtractedEmailEntity]
    routing_suggestion: str    # "K12-UW-Queue" | "HigherEd-UW-Queue" | "Renewals-Queue" | "Manual"
    urgency: str               # "Normal" | "High" | "Urgent"
    urgency_signals: list[str] # ["expiring in 2 days", "urgent"]
    summary: str               # 1-2 sentence plain-English summary of the email
    model_version: str
    prompt_version: str
    latency_ms: int
    correlation_id: Optional[str]

class AiEmailTriageAdapter(ABC):
    @abstractmethod
    async def triage_async(self, request: EmailTriageRequest) -> EmailTriageResult:
        ...
```

### C# Wrapper

```csharp
namespace KSquare.AiEmailTriage.Contracts;

public interface IAiEmailTriageAdapter
{
    Task<EmailTriageResult> TriageAsync(
        EmailTriageRequest request,
        CancellationToken ct = default);
}
```

---

## DI Registration

```csharp
// Program.cs in any consuming service
builder.Services.AddHttpClient<IAiEmailTriageAdapter, AiEmailTriageHttpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["KSquare:AiEmailTriage:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("x-functions-key", builder.Configuration["AiEmailTriage--FunctionKey"]);
});
```

The wrapper is typically consumed by the service or Function that handles `EmailReceivedEvent`
from Component 07 and publishes `EmailTriagedEvent` back onto Component 02 (`KSquare.EventBus`).

---

## Prompt Design

```python
TRIAGE_SYSTEM_PROMPT = """
You are an email triage assistant for an education insurance underwriting platform.
Your task is to analyze incoming broker emails and extract structured information.

You must respond ONLY with valid JSON matching the schema provided. Do not add explanation.

Intent options:
- NewSubmission: broker sending a new account for the first time
- Renewal: broker asking to renew an existing policy
- InfoRequest: broker asking a question or requesting information
- Complaint: policyholder or broker expressing dissatisfaction
- Other: does not fit above categories

Routing options:
- K12-UW-Queue: K-12 school district or public school
- HigherEd-UW-Queue: college, university, or higher education institution
- Renewals-Queue: clearly a renewal request
- Manual: ambiguous; needs human review

Urgency signals to detect: "urgent", "asap", "expiring", "deadline", "today", 
"tomorrow", time expressions within 7 days.

Entity fields to extract (omit if not present):
- institution_name: name of the insured school or district
- broker_firm: name of the broker's agency
- state: US state abbreviation
- effective_date: policy effective date if mentioned
- coverage_types: list of coverage types mentioned (GL, Property, ELL, Student Accident, Cyber, etc.)
- tiv: total insured value if mentioned (numeric)
- enrollment: student enrollment count if mentioned
"""

TRIAGE_USER_TEMPLATE = """
Email subject: {subject}
From: {sender_name} <{sender_email}>
Attachments: {attachment_names}

---
{body_text}
---

Respond with JSON:
{{
  "intent": "<value>",
  "intent_confidence": <0.0-1.0>,
  "routing_suggestion": "<value>",
  "urgency": "<Normal|High|Urgent>",
  "urgency_signals": ["<signal>", ...],
  "summary": "<1-2 sentence summary>",
  "entities": [
    {{ "field_name": "<name>", "value": "<value>", "confidence": <0.0-1.0>, "source_text": "<fragment>" }},
    ...
  ]
}}
"""
```

---

## Implementation

```python
class AzureOpenAiEmailTriageAdapter(AiEmailTriageAdapter):
    def __init__(self, options: AiEmailTriageOptions, tracer: LlmTracer):
        self._client = AsyncAzureOpenAI(
            azure_endpoint=options.azure_openai_endpoint,
            azure_ad_token_provider=get_bearer_token_provider(
                DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default"
            ),
            api_version="2025-01-01-preview"
        )
        self._options = options
        self._tracer = tracer

    async def triage_async(self, request: EmailTriageRequest) -> EmailTriageResult:
        start = time.monotonic()

        # Truncate body to avoid token overrun — triage needs first 2000 chars
        body_truncated = request.body_text[:2000]

        messages = [
            {"role": "system", "content": TRIAGE_SYSTEM_PROMPT},
            {"role": "user", "content": TRIAGE_USER_TEMPLATE.format(
                subject=request.subject,
                sender_name=request.sender_name or "",
                sender_email=request.sender_email,
                attachment_names=", ".join(request.attachment_names) or "none",
                body_text=body_truncated
            )}
        ]

        response = await self._client.chat.completions.create(
            model=self._options.deployment_name,  # "gpt-4o-mini"
            messages=messages,
            temperature=0.0,       # deterministic extraction
            max_tokens=800,
            response_format={"type": "json_object"}
        )

        latency_ms = int((time.monotonic() - start) * 1000)
        content = response.choices[0].message.content
        data = json.loads(content)

        self._tracer.record(
            operation="email_triage",
            model=self._options.deployment_name,
            prompt_tokens=response.usage.prompt_tokens,
            completion_tokens=response.usage.completion_tokens,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id
        )

        return EmailTriageResult(
            email_id=request.email_id,
            intent=data["intent"],
            intent_confidence=data["intent_confidence"],
            extracted_entities=[
                ExtractedEmailEntity(**e) for e in data.get("entities", [])
            ],
            routing_suggestion=data["routing_suggestion"],
            urgency=data["urgency"],
            urgency_signals=data.get("urgency_signals", []),
            summary=data["summary"],
            model_version=response.model,
            prompt_version=self._options.prompt_version,
            latency_ms=latency_ms,
            correlation_id=request.correlation_id
        )
```

---

## Mock Provider (Rule-Based Fallback)

```python
class MockEmailTriageAdapter(AiEmailTriageAdapter):
    """
    Keyword-based triage for demo environments or cost-conscious customers.
    No LLM calls. Deterministic and free.
    """
    RENEWAL_KEYWORDS = ["renewal", "renew", "expiring policy", "up for renewal"]
    COMPLAINT_KEYWORDS = ["complaint", "unacceptable", "disappointed", "not satisfied"]
    K12_KEYWORDS = ["school district", "k-12", "elementary", "middle school", "high school", "isd", "usd"]
    HIGHER_ED_KEYWORDS = ["university", "college", "community college", "higher ed"]
    URGENCY_KEYWORDS = ["urgent", "asap", "expiring", "deadline", "today", "tomorrow"]

    async def triage_async(self, request: EmailTriageRequest) -> EmailTriageResult:
        text = (request.subject + " " + request.body_text).lower()

        intent = "Other"
        if any(kw in text for kw in self.RENEWAL_KEYWORDS):
            intent = "Renewal"
        elif any(kw in text for kw in self.COMPLAINT_KEYWORDS):
            intent = "Complaint"
        elif request.attachment_names:  # has attachments → likely new submission
            intent = "NewSubmission"

        routing = "Manual"
        if any(kw in text for kw in self.K12_KEYWORDS):
            routing = "K12-UW-Queue"
        elif any(kw in text for kw in self.HIGHER_ED_KEYWORDS):
            routing = "HigherEd-UW-Queue"
        elif intent == "Renewal":
            routing = "Renewals-Queue"

        urgency_signals = [kw for kw in self.URGENCY_KEYWORDS if kw in text]
        urgency = "Urgent" if len(urgency_signals) >= 2 else ("High" if urgency_signals else "Normal")

        return EmailTriageResult(
            email_id=request.email_id,
            intent=intent,
            intent_confidence=0.70,
            extracted_entities=[],
            routing_suggestion=routing,
            urgency=urgency,
            urgency_signals=urgency_signals,
            summary=f"Email from {request.sender_email} — {intent}.",
            model_version="mock",
            prompt_version="mock",
            latency_ms=0,
            correlation_id=request.correlation_id
        )
```

---

## Azure Function Entry Point

```python
import azure.functions as func

app = func.FunctionApp()

@app.function_name("EmailTriage")
@app.route(route="email/triage", methods=["POST"], auth_level=func.AuthLevel.FUNCTION)
async def email_triage(req: func.HttpRequest) -> func.HttpResponse:
    body = req.get_json()
    request = EmailTriageRequest(**body)
    adapter = _get_adapter()  # resolved from DI / config
    result = await adapter.triage_async(request)
    return func.HttpResponse(json.dumps(asdict(result)), mimetype="application/json")
```

---

## Configuration

```python
@dataclass
class AiEmailTriageOptions:
    provider: str = "AzureOpenAi"     # "AzureOpenAi" | "Mock"
    azure_openai_endpoint: str = ""
    deployment_name: str = "gpt-4o-mini"
    prompt_version: str = "v1"
    max_body_chars: int = 2000
    temperature: float = 0.0
```

---

## Integration with Component 07 (EmailIngestion)

```
EmailIngestion (Component 07) parses MIME, uploads attachments to blob, publishes EmailReceivedEvent.
                                         ↓
AiEmailTriage (this component) subscribes to EmailReceivedEvent:
  - Calls TriageAsync(EmailTriageRequest)
  - Publishes EmailTriagedEvent { emailId, intent, entities, routing, urgency }
                                         ↓
submission-api subscribes to EmailTriagedEvent:
  - If intent = "NewSubmission": create Draft Submission, pre-populate fields from entities
  - Route to correct underwriter queue per routing_suggestion
```

---

## Evaluation Metrics

| Metric | Target | Alert |
|---|---|---|
| Intent classification accuracy | ≥ 90% (offline eval on labeled set) | < 80% |
| Entity extraction F1 (institution_name) | ≥ 0.85 | < 0.75 |
| P95 latency | ≤ 2,000 ms | > 4,000 ms |
| Daily cost (GPT-4o-mini) | < $5/day @ 500 emails | > $20/day |
| Routing accuracy | ≥ 85% | < 75% |

---

## Failure States

| Scenario | Behaviour |
|---|---|
| LLM returns malformed JSON | Retry once; on second failure return intent = "Other", routing = "Manual", urgency = "Normal" — never block email pipeline |
| LLM API unavailable | Return fallback triage result (all "Manual"/"Other") and log warning; email still enters pipeline |
| Body text > max_body_chars | Truncate at max_body_chars with `[truncated]` marker before sending to LLM |
| Empty body text | Skip LLM; return intent = "Other", routing = "Manual" immediately |

---

## Claude Code Build Prompt

```
Build a Python 3.11 Azure Function package called ksquare-ai-email-triage at path:
shared-python/ksquare-ai-email-triage/

This package classifies incoming submission emails by intent (NewSubmission/Renewal/InfoRequest/
Complaint/Other), extracts key entities (institution name, broker, state, coverage types), and
suggests routing to the correct underwriter queue using Azure OpenAI GPT-4o-mini.

Package structure:
  shared-python/ksquare-ai-email-triage/
  ├── function_app.py             ← Azure Function HTTP trigger entry point
  ├── contracts.py                ← EmailTriageRequest, EmailTriageResult, ExtractedEmailEntity
  ├── options.py                  ← AiEmailTriageOptions dataclass
  ├── prompts.py                  ← TRIAGE_SYSTEM_PROMPT + TRIAGE_USER_TEMPLATE constants
  ├── providers/
  │   ├── azure_openai_triage.py  ← AzureOpenAiEmailTriageAdapter
  │   └── mock_triage.py          ← MockEmailTriageAdapter (keyword rules; no LLM)
  ├── factory.py                  ← resolve_adapter(options) factory function
  ├── requirements.txt
  └── tests/
      ├── test_azure_openai_triage.py
      └── test_mock_triage.py

AzureOpenAiEmailTriageAdapter:
  - Use AsyncAzureOpenAI with azure_ad_token_provider (DefaultAzureCredential)
  - temperature=0.0, response_format={"type": "json_object"}
  - Truncate body_text to max_body_chars before sending
  - Parse JSON response into EmailTriageResult
  - On JSON parse error: retry once; on second failure return safe default result

MockEmailTriageAdapter:
  - Keyword matching for intent (RENEWAL_KEYWORDS, COMPLAINT_KEYWORDS)
  - Attachment presence heuristic: attachments present → likely NewSubmission
  - Keyword matching for routing (K12_KEYWORDS, HIGHER_ED_KEYWORDS)
  - Return confidence = 0.70 for all mock results

Tests:
  - AzureOpenAiTriageAdapter uses WireMock/respx to stub Azure OpenAI response
  - Mock returns correct intent when "renewal" keyword present
  - Mock returns K12-UW-Queue routing when "school district" in body
  - Mock returns urgency = "High" when single urgency keyword found
  - Mock returns urgency = "Urgent" when 2+ urgency keywords found
  - Triage result always returns a safe default when LLM returns malformed JSON
  Use pytest + respx (async HTTP mocking).

Requirements:
  openai>=1.30.0
  azure-identity>=1.16.0
  azure-functions>=1.19.0
  pytest
  pytest-asyncio
  respx
```
