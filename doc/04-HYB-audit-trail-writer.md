# Component 04 — Audit Trail Writer

**Library**: `KSquare.AuditTrail`  
**Layer**: Platform Infrastructure  
**Default Provider**: Azure SQL (append-only table)  
**Alternate Providers**: Azure Monitor / Log Analytics, Cosmos DB (future)  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Audit trails are required in every module of the UW workbench — submission changes,
assignment changes, status transitions, document uploads, referral decisions, bind approvals.
Without a shared library, each service invents its own audit model and misses fields.

The complexity justifying a library:
- Append-only semantics (no UPDATE or DELETE on audit records ever)
- Before/after value diff serialization with PII field masking
- Actor resolution from HTTP context or event context (user ID + display name)
- Structured JSON event schema for downstream analytics/Power BI consumption
- Correlation ID linking audit entries across service boundaries

---

## Interface Contract

```csharp
namespace KSquare.AuditTrail.Contracts;

public interface IAuditTrailWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);

    // Convenience overload for state changes with before/after diff
    Task WriteChangeAsync<T>(
        string resourceType,
        string resourceId,
        string action,
        T? before,
        T? after,
        AuditActor actor,
        string? correlationId = null,
        CancellationToken ct = default) where T : class;

    // Query (read path — optional, for admin/review screens)
    IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.AuditTrail.Models;

public record AuditEntry
{
    public Guid EntryId { get; init; } = Guid.NewGuid();
    public required string ResourceType { get; init; }    // "Submission", "Quote", "Referral"
    public required string ResourceId { get; init; }      // GUID of the resource
    public required string Action { get; init; }          // "Created", "StatusChanged", "Assigned"
    public required AuditActor Actor { get; init; }
    public string? Before { get; init; }                  // JSON snapshot before change (nullable)
    public string? After { get; init; }                   // JSON snapshot after change (nullable)
    public string? CorrelationId { get; init; }
    public string? ServiceName { get; init; }             // which service wrote this
    public IDictionary<string, string>? Tags { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public record AuditActor(
    string UserId,
    string DisplayName,
    string? Role = null,
    AuditActorType ActorType = AuditActorType.User
);

public enum AuditActorType { User, System, ServiceAccount }

public record AuditQuery(
    string? ResourceType = null,
    string? ResourceId = null,
    string? ActorUserId = null,
    string? Action = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50
);
```

---

## Configuration

```csharp
public class AuditTrailOptions
{
    public AuditProvider Provider { get; set; } = AuditProvider.SqlServer;
    public string? ConnectionString { get; set; }
    public string ServiceName { get; set; } = "unknown";
    public bool MaskPiiInBeforeAfter { get; set; } = true;
    public IList<string> PiiFieldNames { get; set; } = ["email", "phone", "taxId", "ssn"];
}

public enum AuditProvider { SqlServer, LogAnalytics, InMemory }
```

---

## DI Registration

```csharp
// Program.cs
builder.Services.AddKsAuditTrail(options =>
{
    options.Provider = AuditProvider.SqlServer;
    options.ConnectionString = builder.Configuration.GetConnectionString("UwDb");
    options.ServiceName = "ue-uw-submission-api";
    options.MaskPiiInBeforeAfter = true;
});
```

---

## Usage Examples

```csharp
// Simple audit write
await audit.WriteAsync(new AuditEntry
{
    ResourceType = "Submission",
    ResourceId = submission.SubmissionId.ToString(),
    Action = "StatusChanged",
    Actor = new AuditActor(userId, displayName, role: "UNDERWRITER"),
    Before = JsonSerializer.Serialize(new { Status = oldStatus }),
    After = JsonSerializer.Serialize(new { Status = newStatus }),
    CorrelationId = correlationId
});

// Convenience diff overload
await audit.WriteChangeAsync(
    resourceType: "Submission",
    resourceId: submission.SubmissionId.ToString(),
    action: "InstitutionUpdated",
    before: oldInstitution,
    after: newInstitution,
    actor: new AuditActor(userId, displayName)
);
// WriteChangeAsync auto-serializes before/after to JSON and masks PII fields

// Query for UW History screen
await foreach (var entry in audit.QueryAsync(new AuditQuery(
    ResourceType: "Submission",
    ResourceId: submissionId.ToString(),
    PageSize: 50)))
{
    // stream entries to caller
}
```

---

## SQL Schema

```sql
CREATE TABLE audit_trail (
    entry_id        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    resource_type   NVARCHAR(100) NOT NULL,
    resource_id     NVARCHAR(500) NOT NULL,
    action          NVARCHAR(200) NOT NULL,
    actor_user_id   NVARCHAR(500) NOT NULL,
    actor_name      NVARCHAR(500) NOT NULL,
    actor_role      NVARCHAR(200) NULL,
    actor_type      NVARCHAR(50) NOT NULL DEFAULT 'User',
    before_json     NVARCHAR(MAX) NULL,
    after_json      NVARCHAR(MAX) NULL,
    correlation_id  NVARCHAR(200) NULL,
    service_name    NVARCHAR(200) NULL,
    tags_json       NVARCHAR(MAX) NULL,
    occurred_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    INDEX IX_audit_resource (resource_type, resource_id, occurred_at DESC),
    INDEX IX_audit_actor    (actor_user_id, occurred_at DESC),
    INDEX IX_audit_occurred (occurred_at DESC)
);
-- NOTE: No UPDATE or DELETE permissions should be granted on this table in production.
```

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.AuditTrail at path: shared/KSquare.AuditTrail/

This library provides append-only structured audit event writing and querying.
Zero domain logic. No UPDATE or DELETE operations ever on audit records.

Project structure:
  shared/KSquare.AuditTrail/
  ├── KSquare.AuditTrail.csproj
  ├── Contracts/
  │   └── IAuditTrailWriter.cs
  ├── Models/
  │   ├── AuditEntry.cs
  │   ├── AuditActor.cs
  │   ├── AuditActorType.cs (enum)
  │   └── AuditQuery.cs
  ├── Configuration/
  │   └── AuditTrailOptions.cs
  ├── Providers/
  │   ├── SqlServerAuditTrailWriter.cs
  │   └── InMemoryAuditTrailWriter.cs
  ├── Internal/
  │   └── PiiMaskingSerializer.cs        ← masks fields by name before JSON serialize
  ├── Database/
  │   ├── AuditDbContext.cs
  │   └── Migrations/
  └── Extensions/
      └── ServiceCollectionExtensions.cs

IAuditTrailWriter implementation:

WriteAsync (SqlServer):
  - INSERT INTO audit_trail with all fields from AuditEntry
  - If MaskPiiInBeforeAfter: run PiiMaskingSerializer on before/after JSON before insert
  - Set ServiceName from options if not set on entry
  - NEVER update or delete existing rows

WriteChangeAsync<T>:
  - Use System.Text.Json to serialize before and after to JSON strings
  - Run PiiMaskingSerializer on both
  - Build AuditEntry and call WriteAsync

QueryAsync (SqlServer):
  - Build parameterized SQL based on AuditQuery filters
  - Use IAsyncEnumerable<AuditEntry> with yield return
  - Respect pagination (OFFSET / FETCH NEXT)
  - Order by occurred_at DESC

PiiMaskingSerializer:
  - Accept JSON string + list of PII field names (case-insensitive)
  - Walk all JSON properties recursively
  - Replace any property whose name matches a PII field with "***REDACTED***"
  - Return masked JSON string
  - Handle nested objects and arrays

InMemoryAuditTrailWriter:
  - Store in ConcurrentBag<AuditEntry>
  - QueryAsync: filter in-memory

Database:
  - AuditDbContext contains DbSet<AuditEntryRecord> mapped to audit_trail table
  - Include EF Core migration creating the table with indexes
  - Map AuditEntry record to AuditEntryRecord entity class (separate to avoid polluting the domain model)

ServiceCollectionExtensions:
  AddKsAuditTrail(Action<AuditTrailOptions>)
  - Register IAuditTrailWriter as scoped
  - Register AuditDbContext if SQL provider

NuGet packages:
  - Microsoft.EntityFrameworkCore.SqlServer 8.x
  - System.Text.Json

Tests at shared/KSquare.AuditTrail.Tests/ (use InMemory provider):
  - WriteAsync stores entry retrievable via QueryAsync
  - WriteChangeAsync serializes before/after
  - PII masking replaces "email" field with REDACTED
  - PII masking handles nested JSON
  - QueryAsync filters by ResourceType + ResourceId
  - QueryAsync filters by date range
  - QueryAsync pagination returns correct page
  Use xUnit + FluentAssertions.
```
