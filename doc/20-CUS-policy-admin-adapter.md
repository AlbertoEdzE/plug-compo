# Component 20 — Policy Administration Adapter

**Library**: `KSquare.PolicyAdminAdapter`  
**Layer**: Integration / Bind and Issuance  
**Default Provider**: PCAS (Sapiens — customer-provided PAM system)  
**Alternate Providers**: Guidewire PolicyCenter (stub), Duck Creek (stub), Mock  
**Language**: C# / .NET 8  
**Depends On**: Component 02 (EventBus), Component 04 (AuditTrail), Component 14 (RulesEngine)

---

## Why This Is a Pluggable Component

Binding a policy requires calling a **Policy Administration System (PAM/PAS)**. The PAM is
customer-provided — UE uses PCAS (Sapiens), but another customer might use Guidewire PolicyCenter,
Duck Creek Policy, Applied EPIC, or a bespoke in-house system.

Without a provider-neutral adapter:
- `quote-api` hard-codes PCAS HTTP calls, payload shapes, and PCAS-specific status polling
- Swapping to a different PAM is a full service rewrite
- Another customer on the same platform cannot reuse any of the bind logic

`KSquare.PolicyAdminAdapter` owns:
1. A **provider-neutral interface** (`IPolicyAdminAdapter`) for ValidateBind, SubmitBind, GetPolicyStatus
2. **Bind readiness validation** delegated to `KSquare.RulesEngine` "bind-readiness" rule set
   (so rules stay in YAML, not hard-coded in the adapter)
3. A **PCAS provider** that translates the neutral request into Sapiens PCAS payload shape
4. **Polling** of PCAS async issuance status until a policy number is returned
5. **Domain events** (`PolicyBoundEvent`, `BindFailedEvent`) via `KSquare.EventBus`
6. **Audit trail** entries via `KSquare.AuditTrail` on every bind lifecycle step
7. A **Mock provider** for demo environments where no PAM is connected

---

## Interface Contract

```csharp
namespace KSquare.PolicyAdminAdapter.Contracts;

public interface IPolicyAdminAdapter
{
    // Validate whether a quote is ready to bind.
    // Does not call the PAM — runs internal bind readiness rules.
    Task<BindReadinessResult> ValidateBindReadinessAsync(
        BindRequest request,
        CancellationToken ct = default);

    // Submit a bind request to the PAM. Returns immediately with a bind job ID.
    // The caller receives PolicyBoundEvent or BindFailedEvent via Service Bus when complete.
    Task<BindJob> SubmitBindAsync(
        BindRequest request,
        CancellationToken ct = default);

    // Poll bind job status (for synchronous callers).
    Task<BindJob> GetBindStatusAsync(
        string bindJobId,
        CancellationToken ct = default);
}

public interface IPolicyAdminPayloadBuilder
{
    // Build the PAM-specific payload from the provider-neutral BindRequest.
    PolicyAdminPayload Build(BindRequest request);
}

public interface IBindReadinessValidator
{
    // Delegate to RulesEngine "bind-readiness" rule set.
    Task<BindReadinessResult> ValidateAsync(BindRequest request, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.PolicyAdminAdapter.Models;

// Provider-neutral bind request — built by quote-api from quote + submission data
public record BindRequest
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string InstitutionLegalName { get; init; }
    public required string InstitutionDba { get; init; }
    public required string NaicsCode { get; init; }
    public required Address InstitutionAddress { get; init; }
    public required string ProducerLicenseNumber { get; init; }
    public required string ProducerCode { get; init; }
    public required string ProducerName { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required IReadOnlyList<BindCoverageLine> CoverageLines { get; init; }
    public required decimal TotalAnnualPremium { get; init; }
    public string? BrokerEmail { get; init; }
    public string? UnderwriterUserId { get; init; }
    public string? SpecialConditions { get; init; }
    public string? CorrelationId { get; init; }
}

public record BindCoverageLine
{
    public required string ProductCode { get; init; }    // "GL", "PROP", "ELL", "SA"
    public required string ProductName { get; init; }
    public required decimal Limit { get; init; }
    public required decimal Retention { get; init; }
    public required decimal AnnualPremium { get; init; }
    public decimal? AggregateLimit { get; init; }
    public string? CoverageConditions { get; init; }
}

public record Address
{
    public required string Line1 { get; init; }
    public string? Line2 { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string Zip { get; init; }
}

public record BindJob
{
    public required string BindJobId { get; init; }
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required BindJobStatus Status { get; init; }
    public string? ProviderTransactionId { get; init; }  // PAM-internal reference
    public string? PolicyNumber { get; init; }           // set when Bound
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryCount { get; init; } = 0;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
}

public record BindReadinessResult
{
    public required bool IsReady { get; init; }
    public IReadOnlyList<BindReadinessIssue> Issues { get; init; } = [];
}

public record BindReadinessIssue(BindIssueLevel Level, string Code, string Message);

// Domain events
public record PolicyBoundEvent
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string BindJobId { get; init; }
    public required string PolicyNumber { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required decimal TotalAnnualPremium { get; init; }
    public required DateTimeOffset BoundAt { get; init; }
    public string? CorrelationId { get; init; }
}

public record BindFailedEvent
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string BindJobId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required DateTimeOffset FailedAt { get; init; }
    public string? CorrelationId { get; init; }
}

// Raw provider payload — internal to adapter
public record PolicyAdminPayload(IDictionary<string, object?> Payload);

public enum BindJobStatus { Pending, Submitted, Processing, Bound, Failed, Cancelled }
public enum BindIssueLevel { Warning, Error }
public enum PolicyAdminProvider { Pcas, Guidewire, Mock }
```

---

## Configuration

```csharp
public class PolicyAdminAdapterOptions
{
    public PolicyAdminProvider Provider { get; set; } = PolicyAdminProvider.Pcas;

    // PCAS / Sapiens
    public string? PcasBaseUrl { get; set; }
    public string? PcasApiKey { get; set; }            // from Key Vault
    public string? PcasEnvironment { get; set; } = "production";
    public string? PcasLineOfBusiness { get; set; } = "ED";
    public string? PcasTransactionType { get; set; } = "NEW_BUSINESS";

    // Polling
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxPollingAttempts { get; set; } = 30;  // 30 × 10s = 5 min max

    // Retry
    public int MaxRetryAttempts { get; set; } = 3;

    // Service Bus topics
    public string BoundEventTopic { get; set; } = "policy-events";
    public string FailedEventTopic { get; set; } = "policy-events";
}
```

---

## DI Registration

```csharp
builder.Services.AddKsPolicyAdminAdapter(options =>
{
    builder.Configuration.GetSection("KSquare:PolicyAdminAdapter").Bind(options);
    options.PcasApiKey = builder.Configuration["Pcas--ApiKey"];
});
// Requires KSquare.EventBus, KSquare.AuditTrail, and KSquare.RulesEngine to be registered.
```

---

## PCAS Payload Builder

The PCAS-specific implementation lives entirely inside the `PcasBindAdapter` and `PcasPayloadBuilder` classes.
A customer replacing PCAS with Guidewire registers `GuidewirePayloadBuilder` instead — no other code changes.

```csharp
public class PcasPayloadBuilder : IPolicyAdminPayloadBuilder
{
    public PolicyAdminPayload Build(BindRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["transactionType"] = _options.PcasTransactionType,    // "NEW_BUSINESS"
            ["lineOfBusiness"]  = _options.PcasLineOfBusiness,     // "ED"
            ["effectiveDate"]   = request.EffectiveDate.ToString("yyyy-MM-dd"),
            ["expirationDate"]  = request.ExpirationDate.ToString("yyyy-MM-dd"),
            ["insured"] = new
            {
                legalName   = request.InstitutionLegalName,
                dba         = request.InstitutionDba,
                address1    = request.InstitutionAddress.Line1,
                city        = request.InstitutionAddress.City,
                state       = request.InstitutionAddress.State,
                zip         = request.InstitutionAddress.Zip,
                naicsCode   = request.NaicsCode
            },
            ["producer"] = new
            {
                licenseNumber = request.ProducerLicenseNumber,
                producerCode  = request.ProducerCode,
                name          = request.ProducerName
            },
            ["coverages"] = request.CoverageLines.Select(c => new
            {
                coverageCode   = MapProductCodeToPcas(c.ProductCode),
                limit          = c.Limit,
                retention      = c.Retention,
                annualPremium  = c.AnnualPremium,
                aggregateLimit = c.AggregateLimit,
                conditions     = c.CoverageConditions ?? ""
            }).ToList(),
            ["totalAnnualPremium"] = request.TotalAnnualPremium,
            ["specialConditions"]  = request.SpecialConditions ?? ""
        };

        return new PolicyAdminPayload(payload);
    }

    // PCAS coverage code mapping — PCAS-specific, lives here not in the interface
    private static string MapProductCodeToPcas(string productCode) => productCode switch
    {
        "GL"   => "CGL",
        "PROP" => "CPP",
        "ELL"  => "ELL",
        "SA"   => "SAC",
        _      => productCode
    };
}
```

---

## Processing Flow

```
1. quote-api calls IPolicyAdminAdapter.ValidateBindReadinessAsync(request)
   → IBindReadinessValidator delegates to KSquare.RulesEngine "bind-readiness" rule set
   → Returns BindReadinessResult { IsReady, Issues[] }
   → quote-api blocks bind if IsReady = false

2. quote-api calls IPolicyAdminAdapter.SubmitBindAsync(request)

3. PcasBindAdapter.SubmitBindAsync:
   a. Build payload via IPolicyAdminPayloadBuilder (PCAS-specific mapping)
   b. INSERT bind_jobs record: Status = Pending
   c. POST {PcasBaseUrl}/api/v2/policies/bind (JSON body)
   d. PCAS returns { transactionId: "pcas-txn-xxx", status: "processing" }
   e. UPDATE bind_jobs: Status = Submitted, ProviderTransactionId = "pcas-txn-xxx"
   f. Write AuditTrail: Action = "BindSubmitted", ResourceId = quoteId
   g. Return bind job record immediately (non-blocking)

4. BindPollingHostedService (IHostedService):
   a. Every PollingInterval: SELECT jobs WHERE status IN ('Pending', 'Submitted', 'Processing')
   b. For each: GET {PcasBaseUrl}/api/v2/policies/{transactionId}/status
   c. If "issued":
      → PolicyNumber = response.policyNumber
      → UPDATE bind_jobs: Status = Bound, PolicyNumber, CompletedAt
      → Write AuditTrail: Action = "PolicyBound"
      → Publish PolicyBoundEvent via IEventPublisher
   d. If "failed":
      → Increment RetryCount
      → If < MaxRetryAttempts: re-submit; else Status = Failed, Publish BindFailedEvent
   e. Catch all exceptions per job; log and continue (never crash host)

5. quote-api subscribes to PolicyBoundEvent:
   a. Update Quote.PolicyNumber and Quote.Status = Bound
   b. Trigger broker notification via KSquare.Notifications
```

---

## SQL Schema

```sql
CREATE TABLE bind_jobs (
    bind_job_id             NVARCHAR(64) NOT NULL PRIMARY KEY,
    quote_id                NVARCHAR(64) NOT NULL,
    submission_id           NVARCHAR(64) NOT NULL,
    provider                NVARCHAR(50) NOT NULL,
    provider_transaction_id NVARCHAR(200) NULL,
    status                  NVARCHAR(30) NOT NULL DEFAULT 'Pending',
    policy_number           NVARCHAR(100) NULL,
    retry_count             INT NOT NULL DEFAULT 0,
    error_code              NVARCHAR(100) NULL,
    error_message           NVARCHAR(MAX) NULL,
    payload_json            NVARCHAR(MAX) NULL,    -- stored for audit + retry rehydration
    created_at              DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    completed_at            DATETIMEOFFSET NULL,
    INDEX IX_bind_quote  (quote_id),
    INDEX IX_bind_status (status, created_at)
);
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Bind readiness check fails | Return `BindReadinessResult { IsReady = false }` — caller blocks; no PAM call made |
| PAM API unavailable | Polly retry 3×; mark job Failed; publish BindFailedEvent |
| PAM returns validation error (e.g., invalid producer code) | Mark Failed immediately; include error code from PAM response |
| Polling exceeds MaxPollingAttempts | Mark Failed with TIMEOUT; alert ops |
| Unknown PAM status value | Log warning; treat as Processing; continue polling |
| Audit trail write fails | Log error; do NOT block bind — audit is best-effort |

---

## Adding a New PAM Provider

To add Guidewire PolicyCenter:

```csharp
// 1. Implement IPolicyAdminPayloadBuilder for Guidewire's schema
public class GuidewirePayloadBuilder : IPolicyAdminPayloadBuilder { ... }

// 2. Implement IPolicyAdminAdapter with Guidewire HTTP calls
public class GuidewireBindAdapter : IPolicyAdminAdapter { ... }

// 3. Register in ServiceCollectionExtensions
case PolicyAdminProvider.Guidewire:
    services.AddSingleton<IPolicyAdminPayloadBuilder, GuidewirePayloadBuilder>();
    services.AddSingleton<IPolicyAdminAdapter, GuidewireBindAdapter>();
    break;

// 4. Configure in appsettings.json
"KSquare:PolicyAdminAdapter": { "Provider": "Guidewire", "GuidewireBaseUrl": "..." }
```

No other code changes required.

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.PolicyAdminAdapter at path: shared/KSquare.PolicyAdminAdapter/

This library provides a provider-neutral interface for submitting bind requests to any Policy
Administration System (PAM). The default provider is PCAS (Sapiens). The library validates bind
readiness via RulesEngine, maps the provider-neutral BindRequest to the PAM's payload schema,
submits the bind, polls for a policy number, and publishes PolicyBoundEvent or BindFailedEvent.

Project structure:
  shared/KSquare.PolicyAdminAdapter/
  ├── KSquare.PolicyAdminAdapter.csproj
  ├── Contracts/
  │   ├── IPolicyAdminAdapter.cs
  │   ├── IPolicyAdminPayloadBuilder.cs
  │   └── IBindReadinessValidator.cs
  ├── Models/
  │   ├── BindRequest.cs
  │   ├── BindCoverageLine.cs
  │   ├── Address.cs
  │   ├── BindJob.cs
  │   ├── BindReadinessResult.cs
  │   ├── BindReadinessIssue.cs
  │   ├── PolicyBoundEvent.cs
  │   ├── BindFailedEvent.cs
  │   ├── PolicyAdminPayload.cs
  │   ├── BindJobStatus.cs (enum)
  │   ├── BindIssueLevel.cs (enum)
  │   └── PolicyAdminProvider.cs (enum)
  ├── Configuration/
  │   └── PolicyAdminAdapterOptions.cs
  ├── Providers/
  │   ├── Pcas/
  │   │   ├── PcasBindAdapter.cs         ← IPolicyAdminAdapter; HTTP + polling
  │   │   └── PcasPayloadBuilder.cs      ← IPolicyAdminPayloadBuilder; PCAS schema
  │   └── Mock/
  │       └── MockPolicyAdminAdapter.cs  ← returns Bound + fake policy number immediately
  ├── Validation/
  │   └── RulesEngineBindValidator.cs    ← IBindReadinessValidator; delegates to IRulesEngine
  ├── HostedService/
  │   └── BindPollingHostedService.cs    ← IHostedService; PeriodicTimer; polls Pending jobs
  ├── Database/
  │   ├── PolicyAdminDbContext.cs
  │   └── Migrations/
  └── Extensions/
      └── ServiceCollectionExtensions.cs

PcasBindAdapter.SubmitBindAsync:
  - Validate: call IBindReadinessValidator.ValidateAsync; throw if !IsReady
  - Build payload via IPolicyAdminPayloadBuilder
  - INSERT bind_jobs: Status = Pending
  - POST {PcasBaseUrl}/api/v2/policies/bind with ApiKey header
  - Parse { transactionId } from response
  - UPDATE bind_jobs: Status = Submitted, ProviderTransactionId
  - Write AuditTrail entry: Action = "BindSubmitted"
  - Return BindJob record

BindPollingHostedService:
  - PeriodicTimer with PollingInterval
  - SELECT bind_jobs WHERE status IN ('Pending', 'Submitted', 'Processing')
  - GET {PcasBaseUrl}/api/v2/policies/{transactionId}/status
  - On "issued": update Bound + PolicyNumber; write AuditTrail; publish PolicyBoundEvent
  - On "failed": increment RetryCount; re-submit or mark Failed + publish BindFailedEvent
  - Never let per-job exception crash the hosted service

MockPolicyAdminAdapter:
  - SubmitBindAsync: return BindJob { Status = Bound, PolicyNumber = "POL-MOCK-{quoteId[..8]}-{year}" }
  - No DB, no HTTP, no events published (optionally publish mock event)

ServiceCollectionExtensions.AddKsPolicyAdminAdapter:
  - Register IPolicyAdminAdapter, IPolicyAdminPayloadBuilder, IBindReadinessValidator
  - Register BindPollingHostedService as IHostedService (unless Mock)
  - Register PolicyAdminDbContext
  - Requires IRulesEngine, IEventPublisher, IAuditTrailWriter

NuGet packages:
  - Microsoft.EntityFrameworkCore.SqlServer 8.x
  - Microsoft.Extensions.Hosting.Abstractions
  - Microsoft.Extensions.Http
  - Polly 8.x

Tests at shared/KSquare.PolicyAdminAdapter.Tests/:
  - SubmitBindAsync persists BindJob with Submitted status and ProviderTransactionId
  - PcasPayloadBuilder maps GL product code to "CGL"
  - PcasPayloadBuilder maps all coverage lines into payload
  - BindPollingHostedService on "issued" status updates BindJob to Bound and sets PolicyNumber
  - BindPollingHostedService on "issued" publishes PolicyBoundEvent with correct PolicyNumber
  - MockAdapter returns Bound status with non-empty PolicyNumber immediately
  - BindReadinessValidator returns IsReady = false when RulesEngine fires a blocking error rule
  Use xUnit + Moq + FluentAssertions + WireMock.Net.
```
