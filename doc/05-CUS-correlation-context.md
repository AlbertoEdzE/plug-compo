# Component 05 — Correlation Context Propagator

**Library**: `KSquare.Correlation`  
**Layer**: Security and Cross-Cutting  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Without correlation context, a bug in production shows isolated log entries from five different
services with no way to trace a single request end-to-end. Debugging becomes guesswork.

In the UW workbench, a single "Create Submission" action touches: APIM →  submission-api →
event bus → IDP function → Service Bus → submission-api again (consumer). Every log entry in every
hop must carry the same correlation ID.

The complexity justifying a library:
- HTTP propagation (read from inbound header, write to outbound HttpClient requests)
- Service Bus propagation (embed in message properties, extract on consume)
- ASP.NET Core middleware for automatic extraction
- HttpClient DelegatingHandler for automatic forwarding
- Structured logging scope integration (Serilog / ILogger)

---

## Interface Contract

```csharp
namespace KSquare.Correlation.Contracts;

public interface ICorrelationContext
{
    string CorrelationId { get; }
    string? TenantId { get; }
    string? UserId { get; }
}

public interface ICorrelationContextAccessor
{
    ICorrelationContext? Current { get; set; }
}
```

---

## Models and Headers

```csharp
public record CorrelationContext(
    string CorrelationId,
    string? TenantId = null,
    string? UserId = null
) : ICorrelationContext;

public static class CorrelationHeaders
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string RequestId     = "X-Request-Id";
    public const string TenantId      = "X-Tenant-Id";
}

public static class ServiceBusProperties
{
    public const string CorrelationId = "correlationId";
    public const string TenantId      = "tenantId";
}
```

---

## DI Registration

```csharp
// Program.cs
builder.Services.AddKsCorrelation();

// Middleware (ASP.NET Core)
app.UseKsCorrelation();

// HttpClient propagation
builder.Services.AddHttpClient<ISubmissionApiClient, SubmissionApiClient>()
    .AddKsCorrelationPropagation();  // adds DelegatingHandler
```

---

## Usage Example

```csharp
// In any service — read current correlation ID
public class SubmissionService(ICorrelationContextAccessor correlation, ILogger<SubmissionService> log)
{
    public async Task CreateAsync(...)
    {
        var cid = correlation.Current?.CorrelationId ?? "none";
        using var scope = log.BeginScope(new { CorrelationId = cid });
        log.LogInformation("Creating submission");  // log entry now includes CorrelationId
    }
}

// In event publishing
await events.PublishAsync(topic, eventType, payload,
    new EventPublishOptions { CorrelationId = correlation.Current?.CorrelationId });

// In event consuming (KSquare.EventBus extracts from message properties automatically)
```

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.Correlation at path: shared/KSquare.Correlation/

Project structure:
  shared/KSquare.Correlation/
  ├── KSquare.Correlation.csproj
  ├── Contracts/
  │   ├── ICorrelationContext.cs
  │   └── ICorrelationContextAccessor.cs
  ├── Models/
  │   ├── CorrelationContext.cs
  │   ├── CorrelationHeaders.cs (static class with header name constants)
  │   └── ServiceBusProperties.cs (static class with SB property name constants)
  ├── Middleware/
  │   └── CorrelationMiddleware.cs          ← ASP.NET Core middleware
  ├── Http/
  │   └── CorrelationPropagationHandler.cs  ← HttpClient DelegatingHandler
  └── Extensions/
      └── ServiceCollectionExtensions.cs

CorrelationMiddleware:
  1. Try to read X-Correlation-Id header from request; if absent, generate Guid.NewGuid().ToString()
  2. Set ICorrelationContextAccessor.Current with CorrelationId, TenantId (from X-Tenant-Id), UserId (from JWT sub claim if available)
  3. Add X-Correlation-Id to the response headers
  4. Begin ILogger structured logging scope: { CorrelationId, TenantId }
  5. Call next

ICorrelationContextAccessor implementation:
  - Use AsyncLocal<CorrelationContext?> for async-safe storage
  - Thread-safe via AsyncLocal

CorrelationPropagationHandler (DelegatingHandler):
  - On SendAsync: read ICorrelationContextAccessor.Current
  - Add X-Correlation-Id and X-Tenant-Id headers to the outgoing HttpRequestMessage if not already present

ServiceCollectionExtensions:
  AddKsCorrelation() — registers ICorrelationContextAccessor as singleton
  UseKsCorrelation(IApplicationBuilder) — registers CorrelationMiddleware
  AddKsCorrelationPropagation(IHttpClientBuilder) — adds CorrelationPropagationHandler

NuGet packages:
  - Microsoft.AspNetCore.Http.Abstractions
  - Microsoft.Extensions.Http (for DelegatingHandler)

Tests at shared/KSquare.Correlation.Tests/:
  - Middleware generates CorrelationId when header absent
  - Middleware reads CorrelationId from header when present
  - Middleware adds CorrelationId to response
  - CorrelationPropagationHandler adds header to outgoing HttpClient requests
  - ICorrelationContextAccessor.Current flows correctly across async awaits (AsyncLocal test)
```
