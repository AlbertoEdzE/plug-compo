# Pluggable Component Library — Analysis and Index

**Scope**: UE Underwriting Workbench + any future insurance/enterprise platform  
**Date**: 2026-05-03  
**Version**: 1.2

---

## Filename Code Legend

File names now include a short category code to make scanning easier:

| Code | Meaning |
|---|---|
| `AZR` | Mostly direct Azure resource usage |
| `HYB` | Custom wrapper or orchestration built on Azure services |
| `CUS` | Mostly custom platform or domain logic |
| `EXT` | External or non-Azure integration |

Example: `13-HYB-llm-provider-adapter.md`

---

## What These Are

Reusable .NET 8 class libraries (and one Python package) for **technical capabilities** that:

1. Have non-trivial internal complexity worth encapsulating  
2. Have a swappable provider (vendor could change across projects)  
3. Can be registered in DI and consumed via a clean interface  
4. Are fully independent of the UW domain model

Each component ships as a project in `shared/` of any monorepo.  
A consuming service adds a project reference and calls `services.AddKs{Component}(...)`.

---

## What These Are NOT

- Domain services (Submission, Quote, Underwriting logic)  
- HTTP clients to raw customer-provided systems (Rating Engine, GhostDraft, PCAS, ODS — those are "blue boxes")
  - **However**: the adapters that wrap these systems and normalize their output **are** our responsibility
    (see Components 18, 19, 20)
  - Salesforce → use Salesforce .NET SDK directly in the owning service  
- Simple API wrappers where the SDK already does the work

---

## Analysis: How Components Were Selected

A capability was included if it met **at least 3** of these criteria:

| Criterion | What it means |
|---|---|
| Non-trivial complexity | More than 100 lines of meaningful logic |
| Provider-swappable | The vendor could change (Azure→AWS, SendGrid→Mailgun) |
| Cross-service reuse | At least 2 services in the workbench will use it |
| Error-handling complexity | Has meaningful retry / fallback / dead-letter / human-review paths |
| Cross-project reuse | Would ship unchanged to a Renewals or Claims project |

---

## Component Index

### Group A — Platform Infrastructure

| # | File | Library Name | What It Does |
|---|---|---|---|
| 01 | [01-AZR-blob-storage-connector.md](01-AZR-blob-storage-connector.md) | `KSquare.BlobStorage` | Upload, download, SAS URL, streaming, lifecycle — provider-agnostic |
| 02 | [02-AZR-event-bus-connector.md](02-AZR-event-bus-connector.md) | `KSquare.EventBus` | Publish + subscribe with transactional outbox pattern, Azure Service Bus default |
| 03 | [03-CUS-idempotency-guard.md](03-CUS-idempotency-guard.md) | `KSquare.Idempotency` | Key-based duplicate prevention for HTTP endpoints and event consumers |
| 04 | [04-HYB-audit-trail-writer.md](04-HYB-audit-trail-writer.md) | `KSquare.AuditTrail` | Append-only structured audit events with actor, resource, action, diff |

### Group B — Security and Cross-Cutting

| # | File | Library Name | What It Does |
|---|---|---|---|
| 05 | [05-CUS-correlation-context.md](05-CUS-correlation-context.md) | `KSquare.Correlation` | Propagate request correlation IDs through HTTP and Service Bus boundaries |
| 06 | [06-CUS-pii-redaction-filter.md](06-CUS-pii-redaction-filter.md) | `KSquare.PiiRedaction` | Detect and mask PII fields in log events, audit records, and event payloads |

### Group C — Communication

| # | File | Library Name | What It Does |
|---|---|---|---|
| 07 | [07-EXT-email-ingestion-connector.md](07-EXT-email-ingestion-connector.md) | `KSquare.EmailIngestion` | Microsoft Graph mailbox polling → parse → attachment blob store → dedup → publish event |
| 08 | [08-EXT-email-send-adapter.md](08-EXT-email-send-adapter.md) | `KSquare.EmailSend` | Outbound email via SendGrid (or SMTP) with Liquid template engine + delivery tracking |
| 09 | [09-CUS-notification-dispatcher.md](09-CUS-notification-dispatcher.md) | `KSquare.Notifications` | Multi-channel notification router: email, in-app bell, future SMS/Teams |

### Group D — Document Intelligence

| # | File | Library Name | What It Does |
|---|---|---|---|
| 10 | [10-AZR-document-extraction-adapter.md](10-AZR-document-extraction-adapter.md) | `KSquare.DocumentExtraction` | Azure AI Document Intelligence OCR + field extraction with confidence scoring and retry |
| 11 | [11-AZR-document-classification-adapter.md](11-AZR-document-classification-adapter.md) | `KSquare.DocumentClassification` | Classify a document as ApplicationForm / LossRun / Financials / Supporting etc. |
| 12 | [12-CUS-extraction-result-mapper.md](12-CUS-extraction-result-mapper.md) | `KSquare.ExtractionMapper` | Map raw extracted key-value pairs to typed domain models via configurable field rules |
| 16 | [16-CUS-risk-analysis-engine.md](16-CUS-risk-analysis-engine.md) | `KSquare.RiskAnalysis` | Loss run aggregation, risk indicator scoring (Campus Safety / Claims Severity / Policy Complexity / Litigation Exposure), and Appetite Fit calculation |

### Group E — Rules and Forms

| # | File | Library Name | What It Does |
|---|---|---|---|
| 14 | [14-CUS-rules-engine-nrules.md](14-CUS-rules-engine-nrules.md) | `KSquare.RulesEngine` | YAML-driven rules for intake routing, referral triggers, appetite scoring, bind readiness |
| 15 | [15-EXT-form-template-engine.md](15-EXT-form-template-engine.md) | `KSquare.FormTemplates` | Populate ACORD / NBI / application form templates from structured input data |

### Group F — AI Agent Platform

| # | File | Library Name | What It Does |
|---|---|---|---|
| 13 | [13-HYB-llm-provider-adapter.md](13-HYB-llm-provider-adapter.md) | `KSquare.AgentOrchestrator` | Full AG UI agent platform: SSE streaming, read-only tool definitions, RAG context assembly, safety guardrails, prompt versioning, online evaluation, conversation audit, human feedback loop |
| 17 | [17-AZR-llm-observability.md](17-AZR-llm-observability.md) | `KSquare.LlmObservability` | RAGAS offline evaluation pipeline, Azure Monitor / LangSmith export, cost tracking, regression alerting, quality dashboard API |
| 22 | [22-HYB-ai-email-triage.md](22-HYB-ai-email-triage.md) | `KSquare.AiEmailTriage` | LLM-powered email intent classification (NewSubmission / Renewal / InfoRequest / Complaint) and entity extraction (institution name, broker, state, coverage types, TIV) from unstructured email body |
| 23 | [23-HYB-intelligent-prefill-engine.md](23-HYB-intelligent-prefill-engine.md) | `KSquare.IntelligentPrefill` | LLM fallback for fields the rule-based ExtractionMapper (12) could not fill; batched extraction with per-field confidence scores and source text fragments for review UI |
| 24 | [24-HYB-document-narrative-engine.md](24-HYB-document-narrative-engine.md) | `KSquare.DocumentNarrative` | Generate four narrative types from structured data: Risk Summary, Loss Run Narrative, Referral Recommendation Memo, Underwriter File Note Draft |
| 25 | [25-HYB-agentic-action-toolkit.md](25-HYB-agentic-action-toolkit.md) | `KSquare.AgenticActions` | Write-side agentic tools (Draft → Confirm → Execute): draft referral, draft field update, draft info request to broker, draft checklist update; plugs into Component 13 ToolRouter |

### Group G — Quote, Proposal, Bind, and Lifecycle

| # | File | Library Name | What It Does |
|---|---|---|---|
| 18 | [18-CUS-rating-adapter.md](18-CUS-rating-adapter.md) | `KSquare.RatingAdapter` | Map coverage pricing requests → UE Rating Engine input schema; call HTTP API with Polly retry + circuit breaker; normalize response to provider-neutral `RatingResult` |
| 19 | [19-EXT-proposal-orchestrator.md](19-EXT-proposal-orchestrator.md) | `KSquare.ProposalOrchestrator` | Submit async NBI/proposal generation job to GhostDraft; poll for completion; store artifact to Blob Storage; publish `ProposalGenerationCompletedEvent` |
| 20 | [20-CUS-policy-admin-adapter.md](20-CUS-policy-admin-adapter.md) | `KSquare.PolicyAdminAdapter` | Provider-neutral bind and policy issuance interface; PCAS/Sapiens is one swappable provider; Guidewire and Duck Creek are alternate providers; validates bind readiness, submits to PAM, polls for policy number, publishes `PolicyBoundEvent` |
| 21 | [21-CUS-state-machine.md](21-CUS-state-machine.md) | `KSquare.StateMachine` | Stateless.NET wrapper for Submission / Quote / Referral state lifecycles; guards invalid transitions; auto-writes AuditTrail and publishes `StateTransitionedEvent` on every transition |

---

## Monorepo Project Structure

Place all libraries in `shared/` of the backend monorepo:

```
ue-uw-backend/
├── shared/                                          ← .NET 8 C# libraries
│   ├── KSquare.BlobStorage/
│   ├── KSquare.EventBus/
│   ├── KSquare.Idempotency/
│   ├── KSquare.AuditTrail/
│   ├── KSquare.Correlation/
│   ├── KSquare.PiiRedaction/
│   ├── KSquare.EmailIngestion/
│   ├── KSquare.EmailSend/
│   ├── KSquare.Notifications/
│   ├── KSquare.DocumentExtraction/          ← C# HTTP wrapper (calls Python IDP function)
│   ├── KSquare.DocumentClassification/      ← C# HTTP wrapper
│   ├── KSquare.ExtractionMapper/
│   ├── KSquare.RulesEngine/
│   ├── KSquare.RiskAnalysis/                ← loss run + risk scoring + appetite fit
│   ├── KSquare.FormTemplates/
│   ├── KSquare.RatingAdapter/               ← maps + calls UE Rating Engine; normalizes result
│   ├── KSquare.ProposalOrchestrator/        ← async GhostDraft job orchestration + blob store
│   ├── KSquare.PolicyAdminAdapter/          ← provider-neutral bind; PCAS = default provider
│   └── KSquare.StateMachine/                ← Stateless.NET lifecycle for Submission/Quote/Referral
├── shared-python/                                   ← Python 3.11 packages (Azure Functions)
│   ├── ksquare-document-extraction/         ← Azure AI Doc Intelligence (IDP Function)
│   ├── ksquare-document-classification/     ← classifier (IDP Function)
│   ├── ksquare-agent-orchestrator/          ← full AG UI agent: read tools, RAG, SSE (AG UI Function)
│   ├── ksquare-agentic-actions/             ← write-side agentic tools: Draft→Confirm→Execute
│   ├── ksquare-ai-email-triage/             ← LLM email intent + entity extraction (IDP Function)
│   ├── ksquare-intelligent-prefill/         ← LLM field extraction fallback (IDP Function)
│   ├── ksquare-document-narrative/          ← risk summary + referral memo + file note generation
│   └── ksquare-llm-observability/           ← RAGAS eval + observability (monitoring Function)
├── src/
│   ├── UeUw.SubmissionApi/    ← KSquare.EventBus, KSquare.AuditTrail, KSquare.RiskAnalysis, KSquare.StateMachine
│   ├── UeUw.QuoteApi/         ← KSquare.RatingAdapter, KSquare.ProposalOrchestrator, KSquare.StateMachine
│   ├── UeUw.UnderwritingApi/  ← KSquare.RulesEngine, KSquare.StateMachine, KSquare.AuditTrail
│   ├── UeUw.DocumentApi/      ← KSquare.BlobStorage, KSquare.DocumentExtraction, KSquare.ExtractionMapper
│   ├── UeUw.CommunicationApi/ ← KSquare.EmailIngestion, KSquare.EmailSend, KSquare.Notifications
│   └── ...
```

---

## Consumption Pattern (all libraries follow this)

```csharp
// 1. Add project reference in .csproj
// <ProjectReference Include="../../shared/KSquare.EmailSend/KSquare.EmailSend.csproj" />

// 2. Register in Program.cs
builder.Services.AddKsEmailSend(options =>
{
    options.Provider = EmailProvider.SendGrid;
    options.ApiKey = builder.Configuration["SendGrid:ApiKey"];
    options.DefaultFromAddress = "noreply@ue-uw.com";
    options.DefaultFromName = "UE Underwriting";
});

// 3. Inject and use
public class SubmissionNotificationService(IEmailSendAdapter email)
{
    public async Task NotifyAsync(string to, string submissionRef)
    {
        await email.SendAsync(new EmailMessage
        {
            To = [to],
            TemplateId = "submission-received",
            Data = new { SubmissionReference = submissionRef }
        });
    }
}
```

---

## Build Order for Backend Teams

Build in this order to unblock downstream services:

```
Phase 1 (cross-cutting, no deps):  05-correlation  →  06-pii-redaction  →  03-idempotency
Phase 2 (infra):                   01-blob-storage  →  02-event-bus  →  04-audit-trail
Phase 3 (communication):           08-email-send  →  07-email-ingestion  →  09-notifications
Phase 4 (intelligence):            10-doc-extraction  →  11-doc-classification  →  12-extraction-mapper
Phase 5 (rules + risk):            14-rules-engine  →  16-risk-analysis  →  15-form-template-engine
Phase 6 (AI agent core):           13-agent-orchestrator  →  17-llm-observability
Phase 7 (quote/bind/lifecycle):    18-rating-adapter  →  19-proposal-orchestrator  →  20-policy-admin-adapter  →  21-state-machine
Phase 8 (AI intelligence layer):   22-ai-email-triage  →  23-intelligent-prefill  →  24-document-narrative  →  25-agentic-actions
```

**Dependency notes for Phase 7:**
- `18-rating-adapter` — no KSquare deps; build standalone first
- `19-proposal-orchestrator` — depends on 01 (BlobStorage) and 02 (EventBus)
- `20-policy-admin-adapter` — depends on 02 (EventBus), 04 (AuditTrail), 14 (RulesEngine)
- `21-state-machine` — depends on 02 (EventBus) and 04 (AuditTrail); build last in phase

**Dependency notes for Phase 8:**
- `22-ai-email-triage` — no KSquare deps; standalone Python package; build first in phase
- `23-intelligent-prefill` — no KSquare deps; standalone Python package
- `24-document-narrative` — depends on output of 16 (RiskAnalysis) as input data; no library dep
- `25-agentic-actions` — extends 13 (AgentOrchestrator) ToolRouter; build after 13 is stable
