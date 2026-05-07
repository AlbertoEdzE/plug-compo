# Component 02 — Event Bus Connector (with Transactional Outbox)

**Library**: `KSquare.EventBus`  
**Layer**: Platform Infrastructure  
**Default Provider**: Azure Service Bus  
**Alternate Providers**: In-memory (test), RabbitMQ (future)  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Without this component, every service that publishes an event has to:
1. Write to the database (submission created)
2. Separately call the Service Bus SDK

If step 2 fails, the event is lost. This is a data consistency bug that shows up in production under load or network blips.

The **Transactional Outbox pattern** solves this: domain state and event are committed in the same DB transaction. A background relay reads the outbox table and delivers events. This pattern is non-trivial to implement correctly and should never be repeated per-service.

Additional complexity:
- Consumer idempotency (protect against duplicate delivery)
- Dead-letter handling with operator visibility
- Correlation ID propagation in message properties
- Type-safe message dispatch/consumption
- Retry with exponential backoff on transient failures

---

## Interface Contract

```csharp
namespace KSquare.EventBus.Contracts;

// Publisher — used by domain services when publishing events
public interface IEventPublisher
{
    // Publish inside a DB transaction (outbox pattern).
    // eventType: e.g. "submission.created", "idp.extraction_complete"
    Task PublishAsync<T>(string topic, string eventType, T payload, 
        EventPublishOptions? options = null, CancellationToken ct = default)
        where T : class;

    // Publish immediately without outbox (fire-and-forget, no delivery guarantee).
    Task PublishDirectAsync<T>(string topic, string eventType, T payload,
        CancellationToken ct = default)
        where T : class;
}

// Consumer — implement this interface per message type
public interface IEventConsumer<TMessage> where TMessage : class
{
    Task ConsumeAsync(EventContext<TMessage> context, CancellationToken ct = default);
}

// Outbox relay — internal background service, exposed for testing
public interface IOutboxRelay
{
    Task ProcessPendingAsync(CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.EventBus.Models;

public class EventPublishOptions
{
    public string? MessageId { get; set; }         // for idempotency key
    public string? CorrelationId { get; set; }
    public string? SessionId { get; set; }
    public TimeSpan? TimeToLive { get; set; }
    public IDictionary<string, string>? Properties { get; set; }
}

public class EventContext<TMessage> where TMessage : class
{
    public required string MessageId { get; init; }
    public required string EventType { get; init; }
    public required string CorrelationId { get; init; }
    public required TMessage Payload { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }
    public int DeliveryCount { get; init; }

    // Call to dead-letter with a reason
    public required Func<string, string, Task> DeadLetterAsync { get; init; }
}

// Outbox DB entity — add to EF Core DbContext of any service that publishes events
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Topic { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }    // JSON
    public required string CorrelationId { get; set; }
    public string? MessageId { get; set; }
    public string? Properties { get; set; }         // JSON
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int RetryCount { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}

public enum OutboxStatus { Pending, Delivered, Failed, DeadLettered }
```

---

## Configuration

```csharp
public class EventBusOptions
{
    public EventBusProvider Provider { get; set; } = EventBusProvider.AzureServiceBus;
    public string? ConnectionString { get; set; }          // from Key Vault
    public bool UseOutbox { get; set; } = true;
    public TimeSpan OutboxPollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int OutboxMaxRetries { get; set; } = 5;
    public string OutboxConnectionString { get; set; } = "";  // DB for outbox table
}

public enum EventBusProvider { AzureServiceBus, InMemory }
```

```json
{
  "KSquare": {
    "EventBus": {
      "Provider": "AzureServiceBus",
      "UseOutbox": true,
      "OutboxPollingInterval": "00:00:05",
      "OutboxMaxRetries": 5
    }
  }
}
```

Key Vault secret: `ServiceBus--ConnectionString`

---

## DI Registration

```csharp
// Program.cs
builder.Services.AddKsEventBus(options =>
{
    builder.Configuration.GetSection("KSquare:EventBus").Bind(options);
    options.ConnectionString = builder.Configuration["ServiceBus--ConnectionString"];
    options.OutboxConnectionString = builder.Configuration.GetConnectionString("UwDb")!;
})
// Register message consumers
.AddConsumer<IdpExtractionCompleteMessage, IdpExtractionConsumer>(
    topic: "submission-events",
    subscription: "submission-api")
.AddConsumer<NotificationMessage, NotificationDispatchConsumer>(
    topic: "notification-events",
    subscription: "communication-api");
```

---

## Usage Examples

```csharp
// Publishing WITH outbox (inside EF Core SaveChanges transaction)
public class SubmissionService(IEventPublisher events, SubmissionDbContext db)
{
    public async Task<Submission> CreateAsync(CreateSubmissionCommand cmd)
    {
        var submission = new Submission { ... };
        db.Submissions.Add(submission);

        // This writes to the outbox table in the SAME transaction
        await events.PublishAsync(
            topic: "submission-events",
            eventType: "submission.created",
            payload: new SubmissionCreatedEvent(submission.SubmissionId, submission.ReferenceNumber),
            options: new EventPublishOptions { CorrelationId = cmd.CorrelationId }
        );

        await db.SaveChangesAsync();  // outbox row + submission committed atomically
        return submission;
    }
}

// Consuming messages
public class IdpExtractionConsumer(ILogger<IdpExtractionConsumer> log)
    : IEventConsumer<IdpExtractionCompleteMessage>
{
    public async Task ConsumeAsync(EventContext<IdpExtractionCompleteMessage> ctx, CancellationToken ct)
    {
        log.LogInformation("Processing extraction {ExtractionId}", ctx.Payload.ExtractionId);
        // ... process
        // If unrecoverable error:
        // await ctx.DeadLetterAsync("reason", "description");
    }
}
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Service Bus unavailable | Outbox holds events; relay retries with backoff; domain stays consistent |
| Consumer throws | Message retried up to lock timeout; then returns to queue; dead-lettered after MaxDeliveryCount |
| Duplicate delivery (redelivery) | `IIdempotencyGuard` (Component 03) prevents re-processing |
| Outbox relay fails | Alerts via ILogger; pending messages stay in outbox; processed on next relay cycle |
| Poison message | `ctx.DeadLetterAsync(...)` moves to DLQ with reason |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.EventBus at path: shared/KSquare.EventBus/

This library implements the Transactional Outbox pattern for reliable event publishing
and provides a type-safe consumer registration framework.

Project structure:
  shared/KSquare.EventBus/
  ├── KSquare.EventBus.csproj
  ├── Contracts/
  │   ├── IEventPublisher.cs
  │   ├── IEventConsumer.cs
  │   └── IOutboxRelay.cs
  ├── Models/
  │   ├── EventPublishOptions.cs
  │   ├── EventContext.cs
  │   ├── OutboxMessage.cs
  │   └── OutboxStatus.cs (enum)
  ├── Configuration/
  │   └── EventBusOptions.cs
  ├── Publishers/
  │   ├── OutboxEventPublisher.cs        ← writes to outbox table in current DbContext TX
  │   └── DirectServiceBusPublisher.cs   ← direct SDK call, no outbox
  ├── Consumers/
  │   ├── ServiceBusConsumerHost.cs      ← IHostedService that manages ServiceBusProcessor per subscription
  │   └── ConsumerRegistration.cs
  ├── Outbox/
  │   ├── OutboxRelay.cs                 ← IHostedService polling outbox table, delivering to SB
  │   ├── OutboxDbContext.cs             ← minimal DbContext containing only OutboxMessage table
  │   └── OutboxMigrations/              ← EF Core migrations for outbox table
  ├── Providers/
  │   ├── AzureServiceBusProvider.cs
  │   └── InMemoryEventBusProvider.cs    ← for testing, no external dependency
  └── Extensions/
      └── ServiceCollectionExtensions.cs

Interface details:

IEventPublisher.PublishAsync:
  - Resolve the current IDbContextTransaction from ambient DbContext (or create one)
  - Serialize payload to JSON (System.Text.Json)
  - Write OutboxMessage row to OutboxDbContext within that transaction
  - Do NOT call Service Bus directly — the OutboxRelay does that
  - If UseOutbox = false, fall through to DirectServiceBusPublisher

OutboxRelay (IHostedService):
  - Poll OutboxDbContext every OutboxPollingInterval for Status = Pending
  - For each pending message: send to Azure Service Bus topic
    - Set MessageId from OutboxMessage.MessageId (enables SB dedup if enabled on topic)
    - Set CorrelationId and EventType as application properties
    - On success: update Status = Delivered, ProcessedAt = now
    - On failure: increment RetryCount, set LastError; if RetryCount >= MaxRetries set Status = Failed
  - Process at most 50 messages per cycle (configurable)
  - Use ILogger for all operations

ServiceBusConsumerHost (IHostedService):
  - One ServiceBusProcessor per registered consumer (topic + subscription)
  - On message received: deserialize payload JSON → TMessage
  - Build EventContext<TMessage> with all properties
  - Resolve IEventConsumer<TMessage> from IServiceProvider (scoped resolution)
  - Call ConsumeAsync; on success: CompleteMessageAsync
  - On consumer exception: AbandonMessageAsync (back to queue for retry)
  - On consumer calling DeadLetterAsync: DeadLetterMessageAsync with reason

InMemoryEventBusProvider:
  - Publishes to ConcurrentQueue per topic
  - Consumer registrations called synchronously
  - Used in unit tests — no Service Bus connection needed

ServiceCollectionExtensions:
  AddKsEventBus(Action<EventBusOptions>) + AddConsumer<TMessage, TConsumer>(topic, subscription)
  - Register OutboxRelay as IHostedService when UseOutbox=true
  - Register ServiceBusConsumerHost as IHostedService per consumer
  - Register IEventPublisher appropriately

NuGet packages:
  - Azure.Messaging.ServiceBus 7.x
  - Microsoft.EntityFrameworkCore.SqlServer 8.x
  - Microsoft.Extensions.Hosting.Abstractions
  - System.Text.Json

Tests at shared/KSquare.EventBus.Tests/:
  Use InMemoryEventBusProvider throughout (no Azure credentials needed)
  - Publish → consumer receives message with correct payload
  - Publish within transaction → OutboxMessage created in DB
  - OutboxRelay processes pending messages → Status becomes Delivered
  - Consumer exception → message abandoned (retry path)
  - Consumer calls DeadLetterAsync → message dead-lettered
  - Duplicate message (same MessageId) → consumer called only once (pair with Idempotency guard test)
```
