# Component 19 — Proposal Generation Orchestrator

**Library**: `KSquare.ProposalOrchestrator`  
**Layer**: Integration / Quote  
**Default Provider**: GhostDraft (async NBI/proposal generation)  
**Alternate Providers**: DocuSign CLM, iText7 (local generation), Mock  
**Language**: C# / .NET 8  
**Depends On**: Component 01 (BlobStorage), Component 02 (EventBus)

---

## Why This Is a Pluggable Component

Generating a quote proposal (NBI — New Business Instructions) is fundamentally different from
filling a PDF form template (Component 15). GhostDraft is a cloud document generation service that:

1. Accepts a structured data payload (quote + submission + coverage terms)
2. Asynchronously renders a multi-page branded proposal document (PDF or Word)
3. Returns a generation job ID — the document is NOT ready immediately
4. Requires polling or a webhook callback to know when generation is complete
5. Returns a download URL for the generated document

Component 15 (FormTemplates) handles synchronous AcroForm field-filling of known PDFs.
This component handles async long-running generation with job tracking, retry on failure,
blob storage of the result, and a domain event when the proposal is ready.

Without a shared library, quote-api hard-codes GhostDraft SDK calls, making provider changes
(GhostDraft → DocuSign CLM → in-house) a full service rewrite.

---

## Interface Contract

```csharp
namespace KSquare.ProposalOrchestrator.Contracts;

public interface IProposalOrchestrator
{
    // Submit a proposal generation job. Returns immediately with a job ID.
    // The caller receives ProposalGenerationCompleted event via Service Bus when done.
    Task<ProposalGenerationJob> StartGenerationAsync(
        ProposalGenerationRequest request,
        CancellationToken ct = default);

    // Poll job status (for synchronous callers that need the result inline).
    Task<ProposalGenerationJob> GetJobStatusAsync(
        string jobId,
        CancellationToken ct = default);

    // Called by webhook handler or polling background service when provider reports completion.
    Task<ProposalArtifact> CompleteJobAsync(
        string jobId,
        string providerDocumentUrl,
        CancellationToken ct = default);
}

public interface IProposalPayloadBuilder
{
    // Build the provider-specific payload from internal quote + submission data.
    ProposalProviderPayload Build(ProposalGenerationRequest request);
}
```

---

## Models

```csharp
namespace KSquare.ProposalOrchestrator.Models;

public record ProposalGenerationRequest
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string ProposalType { get; init; }     // "NBI", "QuoteProposal", "Binder"
    public required string InstitutionName { get; init; }
    public required string BrokerName { get; init; }
    public required string BrokerEmail { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required IReadOnlyList<ProposalCoverageLine> CoverageLines { get; init; }
    public string? UnderwriterName { get; init; }
    public string? SpecialConditions { get; init; }
    public string? OutputFormat { get; init; } = "pdf";    // "pdf" or "docx"
    public string? CorrelationId { get; init; }
}

public record ProposalCoverageLine
{
    public required string ProductName { get; init; }
    public required decimal Limit { get; init; }
    public required decimal Retention { get; init; }
    public required decimal AnnualPremium { get; init; }
    public decimal? AggregateLimit { get; init; }
    public string? CoverageConditions { get; init; }
}

public record ProposalGenerationJob
{
    public required string JobId { get; init; }
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required ProposalJobStatus Status { get; init; }
    public string? ProviderJobId { get; init; }            // GhostDraft's internal job ID
    public string? ArtifactBlobPath { get; init; }         // set when complete
    public string? ArtifactSasUrl { get; init; }           // set when complete; expires after SasTtl
    public int RetryCount { get; init; } = 0;
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
}

public record ProposalArtifact
{
    public required string JobId { get; init; }
    public required string QuoteId { get; init; }
    public required string BlobPath { get; init; }
    public required string SasUrl { get; init; }
    public required DateTimeOffset SasExpiry { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSizeBytes { get; init; }
}

// Domain event published when proposal is ready
public record ProposalGenerationCompletedEvent
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string JobId { get; init; }
    public required string BlobPath { get; init; }
    public required string SasUrl { get; init; }
    public required string ProposalType { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public string? CorrelationId { get; init; }
}

public record ProposalProviderPayload(IDictionary<string, object?> Payload);

public enum ProposalJobStatus
{
    Pending,      // job submitted, not yet confirmed by provider
    Processing,   // provider acknowledged, rendering in progress
    Completed,    // document ready and stored to blob
    Failed,       // provider reported failure or max retries exceeded
    Cancelled
}
```

---

## Configuration

```csharp
public class ProposalOrchestratorOptions
{
    public ProposalProvider Provider { get; set; } = ProposalProvider.GhostDraft;

    // GhostDraft
    public string? GhostDraftApiUrl { get; set; }
    public string? GhostDraftApiKey { get; set; }         // from Key Vault
    public string? GhostDraftEnvironment { get; set; } = "production";

    // Template IDs in GhostDraft (per proposal type)
    public IDictionary<string, string> TemplateIdMap { get; set; } = new Dictionary<string, string>
    {
        ["NBI"]           = "ue-nbi-template-v3",
        ["QuoteProposal"] = "ue-quote-proposal-v2",
        ["Binder"]        = "ue-binder-v2"
    };

    // Async polling (used if no webhook configured)
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxPollingAttempts { get; set; } = 60;     // 60 × 5s = 5 minutes max

    // Output blob storage
    public string OutputBlobContainer { get; set; } = "generated-proposals";
    public string OutputPathTemplate { get; set; } = "proposals/{year}/{month}/{quoteId}/{proposalType}-{timestamp}.pdf";
    public TimeSpan SasUrlTtl { get; set; } = TimeSpan.FromHours(24);

    // Retry on failure
    public int MaxRetryAttempts { get; set; } = 3;

    // Service Bus topic for completion event
    public string CompletionEventTopic { get; set; } = "proposal-events";
}

public enum ProposalProvider { GhostDraft, IText, Mock }
```

---

## DI Registration

```csharp
builder.Services.AddKsProposalOrchestrator(options =>
{
    builder.Configuration.GetSection("KSquare:ProposalOrchestrator").Bind(options);
    options.GhostDraftApiKey = builder.Configuration["GhostDraft--ApiKey"];
})
// Requires KSquare.BlobStorage and KSquare.EventBus to be registered.
;
```

---

## Processing Flow

```
1. quote-api calls IProposalOrchestrator.StartGenerationAsync(request)

2. GhostDraftProposalOrchestrator:
   a. Build payload via IProposalPayloadBuilder (map internal fields → GhostDraft schema)
   b. POST /api/v3/documents/generate
      Body: { templateId, data: { ... }, outputFormat: "pdf" }
   c. GhostDraft returns { jobId: "gd-job-xxx", status: "queued" }
   d. Persist ProposalGenerationJob { Status = Pending, ProviderJobId = "gd-job-xxx" }
   e. Return job record to quote-api immediately (do not block)

3. ProposalPollingHostedService (IHostedService):
   a. Every PollingInterval: fetch all Pending/Processing jobs
   b. For each: GET /api/v3/documents/{providerJobId}/status
   c. If status = "completed":
      → Download document from GhostDraft download URL
      → Upload to Blob Storage via IBlobStorageConnector
      → Generate SAS URL
      → Update job: Status = Completed, ArtifactBlobPath, ArtifactSasUrl
      → Publish ProposalGenerationCompletedEvent via IEventPublisher
   d. If status = "failed":
      → Increment RetryCount
      → If RetryCount < MaxRetryAttempts: re-submit to GhostDraft
      → Else: Status = Failed, log error

4. quote-api subscribes to ProposalGenerationCompletedEvent:
   a. Link ProposalArtifact to quote record
   b. Notify broker via KSquare.Notifications
```

---

## GhostDraft Payload Builder

```csharp
public class GhostDraftPayloadBuilder : IProposalPayloadBuilder
{
    public ProposalProviderPayload Build(ProposalGenerationRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["templateId"]   = GetTemplateId(request.ProposalType),
            ["outputFormat"] = request.OutputFormat ?? "pdf",
            ["data"] = new Dictionary<string, object?>
            {
                // Header fields
                ["insuredName"]       = request.InstitutionName,
                ["brokerName"]        = request.BrokerName,
                ["brokerEmail"]       = request.BrokerEmail,
                ["effectiveDate"]     = request.EffectiveDate.ToString("MMMM dd, yyyy"),
                ["expirationDate"]    = request.ExpirationDate.ToString("MMMM dd, yyyy"),
                ["underwriterName"]   = request.UnderwriterName ?? "Underwriting Team",
                ["quoteReference"]    = request.QuoteId,
                ["specialConditions"] = request.SpecialConditions ?? "",
                ["generatedDate"]     = DateTimeOffset.UtcNow.ToString("MMMM dd, yyyy"),

                // Coverage table — GhostDraft expects a repeating section
                ["coverageLines"] = request.CoverageLines.Select(c => new Dictionary<string, object?>
                {
                    ["productName"]  = c.ProductName,
                    ["limit"]        = c.Limit.ToString("C0"),
                    ["retention"]    = c.Retention.ToString("C0"),
                    ["premium"]      = c.AnnualPremium.ToString("C2"),
                    ["aggregate"]    = c.AggregateLimit?.ToString("C0") ?? "N/A",
                    ["conditions"]   = c.CoverageConditions ?? ""
                }).ToList(),

                // Totals
                ["totalPremium"] = request.CoverageLines.Sum(c => c.AnnualPremium).ToString("C2")
            }
        };

        return new ProposalProviderPayload(payload);
    }
}
```

---

## SQL Schema

```sql
CREATE TABLE proposal_generation_jobs (
    job_id              NVARCHAR(64) NOT NULL PRIMARY KEY,
    quote_id            NVARCHAR(64) NOT NULL,
    submission_id       NVARCHAR(64) NOT NULL,
    proposal_type       NVARCHAR(50) NOT NULL,
    provider            NVARCHAR(50) NOT NULL,
    provider_job_id     NVARCHAR(200) NULL,
    status              NVARCHAR(30) NOT NULL DEFAULT 'Pending',
    retry_count         INT NOT NULL DEFAULT 0,
    artifact_blob_path  NVARCHAR(1000) NULL,
    artifact_sas_url    NVARCHAR(2000) NULL,
    error_message       NVARCHAR(MAX) NULL,
    created_at          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    completed_at        DATETIMEOFFSET NULL,
    INDEX IX_proposal_quote     (quote_id),
    INDEX IX_proposal_status    (status, created_at)
);
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| GhostDraft API unavailable | Polly retry 3×; mark job Failed after exhaustion; alert ops |
| GhostDraft job fails | Increment RetryCount; re-submit if < MaxRetryAttempts; else Failed |
| Blob upload fails after document downloaded | Retry blob upload; do not re-generate from GhostDraft |
| SAS URL generation fails | Log error; return job as Completed with empty SasUrl; caller generates on demand |
| Polling exceeds MaxPollingAttempts | Mark job Failed with TIMEOUT; alert ops |
| Template ID not found in TemplateIdMap | Throw `ProposalTemplateNotFoundException` at start — fail fast |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.ProposalOrchestrator at path: shared/KSquare.ProposalOrchestrator/

This library orchestrates async proposal/NBI document generation via GhostDraft.
It submits a generation job, polls for completion, stores the result to Blob Storage,
and publishes a ProposalGenerationCompletedEvent to Service Bus.

Project structure:
  shared/KSquare.ProposalOrchestrator/
  ├── KSquare.ProposalOrchestrator.csproj
  ├── Contracts/
  │   ├── IProposalOrchestrator.cs
  │   └── IProposalPayloadBuilder.cs
  ├── Models/
  │   ├── ProposalGenerationRequest.cs
  │   ├── ProposalCoverageLine.cs
  │   ├── ProposalGenerationJob.cs
  │   ├── ProposalArtifact.cs
  │   ├── ProposalGenerationCompletedEvent.cs
  │   ├── ProposalProviderPayload.cs
  │   └── ProposalJobStatus.cs (enum)
  ├── Configuration/
  │   └── ProposalOrchestratorOptions.cs
  ├── Providers/
  │   ├── GhostDraftProposalOrchestrator.cs   ← StartGenerationAsync + CompleteJobAsync
  │   └── MockProposalOrchestrator.cs          ← synchronous mock; immediately returns completed job
  ├── Mapping/
  │   └── GhostDraftPayloadBuilder.cs          ← build GhostDraft payload from request
  ├── HostedService/
  │   └── ProposalPollingHostedService.cs      ← IHostedService; polls pending jobs on PeriodicTimer
  ├── Database/
  │   ├── ProposalDbContext.cs
  │   └── Migrations/
  └── Extensions/
      └── ServiceCollectionExtensions.cs

GhostDraftProposalOrchestrator.StartGenerationAsync:
  - Build payload via IProposalPayloadBuilder
  - POST to {GhostDraftApiUrl}/api/v3/documents/generate with API key header
  - Parse { jobId } from response
  - INSERT ProposalGenerationJob record with Status = Pending, ProviderJobId = jobId
  - Return job record

ProposalPollingHostedService:
  - PeriodicTimer with PollingInterval
  - Each tick: SELECT jobs WHERE status IN ('Pending', 'Processing')
  - For each: GET {GhostDraftApiUrl}/api/v3/documents/{providerJobId}/status
  - If "completed":
    - GET download URL from response
    - Download document bytes via HttpClient
    - Upload to blob via IBlobStorageConnector with OutputPathTemplate
    - Generate SAS URL via IBlobStorageConnector.GenerateSasUrlAsync
    - Update job: Completed, blob paths
    - Publish ProposalGenerationCompletedEvent via IEventPublisher
  - If "failed": increment RetryCount, re-submit or mark Failed
  - Catch all exceptions per job; log and continue to next job (never crash host)

MockProposalOrchestrator:
  - StartGenerationAsync: create job with Status = Completed immediately
  - Set ArtifactBlobPath = "mock/proposals/{quoteId}.pdf"
  - No actual blob storage call; no event published (or optionally publish mock event)

ServiceCollectionExtensions.AddKsProposalOrchestrator:
  - Register IProposalOrchestrator and IProposalPayloadBuilder
  - Register ProposalPollingHostedService as IHostedService (unless Mock provider)
  - Register ProposalDbContext
  - Requires IBlobStorageConnector and IEventPublisher registered

NuGet packages:
  - Microsoft.EntityFrameworkCore.SqlServer 8.x
  - Microsoft.Extensions.Hosting.Abstractions
  - Microsoft.Extensions.Http
  - Polly 8.x

Tests at shared/KSquare.ProposalOrchestrator.Tests/:
  - StartGenerationAsync persists job with Pending status and ProviderJobId
  - GhostDraftPayloadBuilder includes all coverage lines in payload
  - GhostDraftPayloadBuilder formats TotalPremium as currency string
  - CompleteJobAsync updates job status to Completed and sets BlobPath
  - CompleteJobAsync publishes ProposalGenerationCompletedEvent
  - MockOrchestrator returns Completed status immediately
  Use xUnit + Moq + FluentAssertions + WireMock.Net.
```
