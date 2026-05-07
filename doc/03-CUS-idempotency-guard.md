# Component 03 — Idempotency Guard

**Library**: `KSquare.Idempotency`  
**Layer**: Platform Infrastructure  
**Default Provider**: SQL Server (shares app DB)  
**Alternate Providers**: Redis, In-memory (test)  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Service Bus delivers messages **at least once** — duplicates happen during retries, failovers,
and lock expirations. HTTP clients retry on timeouts — the server may have already processed the
request. Without idempotency guards, duplicate submissions, duplicate emails, and duplicate
events silently corrupt data.

Two use cases in the UW workbench:
1. **HTTP Idempotency**: Client sends `Idempotency-Key: {uuid}` header. If the key was seen,
   return the cached response. Used on `POST /submissions`, `POST /quotes/{id}/bind`, etc.
2. **Consumer Idempotency**: Event consumer checks `messageId` before processing. Used in
   every `IEventConsumer<T>` implementation.

The complexity justifying a library:
- Atomic check-and-set (race condition between two concurrent duplicate requests)
- TTL management per key (configurable; submission keys = 24h, consumer keys = 7 days)
- Serializing and replaying the cached HTTP response body
- Supporting multiple backends with the same interface

---

## Interface Contract

```csharp
namespace KSquare.Idempotency.Contracts;

public interface IIdempotencyGuard
{
    // HTTP use case: check if this key was already processed.
    // Returns null on first call. Returns cached result on subsequent calls.
    Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default);

    // HTTP use case: store the result after successful processing.
    Task SetAsync(string key, IdempotencyResult result, TimeSpan? ttl = null, CancellationToken ct = default);

    // Event consumer use case: atomic check-and-mark.
    // Returns true if this messageId is new (caller should process).
    // Returns false if already processed (caller should skip).
    Task<bool> TryMarkProcessedAsync(string messageId, TimeSpan? ttl = null, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.Idempotency.Models;

public record IdempotencyResult(
    int StatusCode,
    string ResponseBody,           // JSON
    string ContentType,
    DateTimeOffset ProcessedAt
);
```

---

## Configuration

```csharp
public class IdempotencyOptions
{
    public IdempotencyProvider Provider { get; set; } = IdempotencyProvider.SqlServer;
    public string? ConnectionString { get; set; }
    public string? RedisConnectionString { get; set; }
    public TimeSpan DefaultHttpKeyTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan DefaultConsumerKeyTtl { get; set; } = TimeSpan.FromDays(7);
}

public enum IdempotencyProvider { SqlServer, Redis, InMemory }
```

---

## DI Registration + ASP.NET Core Middleware

```csharp
// Program.cs
builder.Services.AddKsIdempotency(options =>
{
    options.Provider = IdempotencyProvider.SqlServer;
    options.ConnectionString = builder.Configuration.GetConnectionString("UwDb");
});

// Register middleware for HTTP idempotency (optional, per-service choice)
app.UseKsIdempotency(headerName: "Idempotency-Key");
```

---

## Usage Examples

```csharp
// Event consumer — skip duplicate messages
public class SubmissionCreatedConsumer(IIdempotencyGuard guard) : IEventConsumer<SubmissionCreatedEvent>
{
    public async Task ConsumeAsync(EventContext<SubmissionCreatedEvent> ctx, CancellationToken ct)
    {
        if (!await guard.TryMarkProcessedAsync(ctx.MessageId, ttl: TimeSpan.FromDays(7), ct))
        {
            // Already processed — safe to skip
            return;
        }
        // Process normally
    }
}

// Manual HTTP key check (if not using middleware)
public async Task<IResult> CreateSubmissionAsync(
    CreateSubmissionRequest req, 
    IIdempotencyGuard guard,
    HttpContext http)
{
    var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
    if (key is not null)
    {
        var cached = await guard.GetAsync(key);
        if (cached is not null)
            return Results.Content(cached.ResponseBody, cached.ContentType, cached.StatusCode);
    }

    var result = await submissionService.CreateAsync(req);
    var responseBody = JsonSerializer.Serialize(result);
    
    if (key is not null)
        await guard.SetAsync(key, new IdempotencyResult(201, responseBody, "application/json", DateTimeOffset.UtcNow));

    return Results.Created($"/submissions/{result.SubmissionId}", result);
}
```

---

## SQL Schema

```sql
CREATE TABLE idempotency_keys (
    key             NVARCHAR(500) NOT NULL PRIMARY KEY,
    status_code     INT NOT NULL,
    response_body   NVARCHAR(MAX) NOT NULL,
    content_type    NVARCHAR(200) NOT NULL,
    processed_at    DATETIMEOFFSET NOT NULL,
    expires_at      DATETIMEOFFSET NOT NULL,
    INDEX IX_idempotency_expires (expires_at)   -- for cleanup job
);

CREATE TABLE idempotency_consumer_keys (
    message_id      NVARCHAR(500) NOT NULL PRIMARY KEY,
    processed_at    DATETIMEOFFSET NOT NULL,
    expires_at      DATETIMEOFFSET NOT NULL,
    INDEX IX_consumer_expires (expires_at)
);
```

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.Idempotency at path: shared/KSquare.Idempotency/

This library provides idempotency protection for HTTP endpoints and event consumers.
It has zero domain logic — pure infrastructure.

Project structure:
  shared/KSquare.Idempotency/
  ├── KSquare.Idempotency.csproj
  ├── Contracts/
  │   └── IIdempotencyGuard.cs
  ├── Models/
  │   └── IdempotencyResult.cs
  ├── Configuration/
  │   └── IdempotencyOptions.cs
  ├── Providers/
  │   ├── SqlServerIdempotencyGuard.cs
  │   ├── RedisIdempotencyGuard.cs
  │   └── InMemoryIdempotencyGuard.cs
  ├── Middleware/
  │   └── IdempotencyMiddleware.cs       ← ASP.NET Core middleware
  ├── Database/
  │   ├── IdempotencyDbContext.cs        ← minimal EF Core context
  │   └── Migrations/
  └── Extensions/
      └── ServiceCollectionExtensions.cs

IIdempotencyGuard implementation details:

SqlServerIdempotencyGuard:
  GetAsync: SELECT from idempotency_keys WHERE key = @key AND expires_at > GETUTCDATE()
  SetAsync: INSERT INTO idempotency_keys (atomic, handle duplicate INSERT gracefully with try/catch)
  TryMarkProcessedAsync: use INSERT ... WHERE NOT EXISTS to atomically check+insert consumer key.
    Returns true if insert succeeded (new), false if already existed (duplicate).
    This atomic SQL prevents race conditions on concurrent duplicate messages.

RedisIdempotencyGuard:
  GetAsync: GET idempotency:{key} (JSON-serialized IdempotencyResult)
  SetAsync: SET idempotency:{key} value EX {ttl_seconds}
  TryMarkProcessedAsync: SET consumer:{messageId} 1 NX EX {ttl_seconds} — returns true if SET succeeded

InMemoryIdempotencyGuard:
  Use ConcurrentDictionary with DateTimeOffset expiry check.
  Use SemaphoreSlim for TryMarkProcessedAsync atomic check.

IdempotencyMiddleware (ASP.NET Core):
  1. Read header specified in options (default: "Idempotency-Key")
  2. If header absent: pass through to next middleware
  3. If header present: call guard.GetAsync(key)
     - If cached: write cached status/body/content-type directly to response, return (skip handler)
     - If not cached: wrap ResponseBody in a capturing MemoryStream
       - Call next middleware
       - On success (2xx): call guard.SetAsync with captured response
       - Copy captured response to real response stream

ServiceCollectionExtensions:
  AddKsIdempotency(Action<IdempotencyOptions>) registers:
    - IIdempotencyGuard (scoped)
    - IdempotencyDbContext if using SQL provider
  Extension method: UseKsIdempotency(this IApplicationBuilder app, string headerName = "Idempotency-Key")

NuGet packages:
  - Microsoft.EntityFrameworkCore.SqlServer 8.x
  - StackExchange.Redis 2.x (optional, only if Redis provider used)
  - Microsoft.AspNetCore.Http.Abstractions

Tests at shared/KSquare.Idempotency.Tests/ (use InMemory provider throughout):
  - First call returns null, SetAsync, second call returns cached result
  - TryMarkProcessedAsync: first call = true, second call = false (concurrent-safe)
  - Expired keys return null (set TTL of 1 second, wait, check)
  - Middleware captures 201 response and replays on duplicate request
  - Middleware skips when no header present
  Use xUnit + FluentAssertions.
```
