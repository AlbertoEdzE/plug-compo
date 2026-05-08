using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Extensions;
using KSquare.AuditTrail.Models;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Extensions;
using KSquare.BlobStorage.Models;
using KSquare.Correlation.Models;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

var assertions = new List<AssertionRecord>();

var submissionId = $"SUB-{Random.Shared.Next(1000, 9999)}";
var correlationId = Guid.NewGuid().ToString("N");

Record("synthesize_ids", !string.IsNullOrWhiteSpace(submissionId) && !string.IsNullOrWhiteSpace(correlationId), $"{submissionId} / {correlationId}");

await Run("correlation_propagates", async () =>
{
    var accessor = new KSquare.Correlation.CorrelationContextAccessor();
    var ctx = new CorrelationContext(correlationId, "tenant-1", "uw-1");
    accessor.Current = ctx;

    await Task.Delay(1);
    if (accessor.Current?.CorrelationId != correlationId)
    {
        throw new InvalidOperationException("CorrelationId did not flow across await.");
    }

    var observed = await Task.Run(() => accessor.Current?.CorrelationId);
    if (observed != correlationId)
    {
        throw new InvalidOperationException("CorrelationId did not flow into Task.Run.");
    }
});

var payloadJson = JsonSerializer.Serialize(new
{
    email = "user@example.com",
    phone = "(555) 123-4567",
    submissionId
});
Record("synthesize_pii_payload", payloadJson.Contains("user@example.com", StringComparison.OrdinalIgnoreCase) && payloadJson.Contains("555", StringComparison.OrdinalIgnoreCase), payloadJson);

var sql = Environment.GetEnvironmentVariable("LAB_SQL_CONNECTION")
          ?? "Server=localhost,1433;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";

await Run("pii_redaction_before_audit", async () =>
{
    await EnsureAuditTrailSchemaAsync(sql);

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddKsAuditTrail(options =>
    {
        options.Provider = AuditProvider.SqlServer;
        options.ConnectionString = sql;
        options.ServiceName = "lab-canvas-1";
        options.MaskPiiInBeforeAfter = true;
        options.PiiFieldNames = new List<string> { "email", "phone" };
    });
    var writer = services.BuildServiceProvider().GetRequiredService<IAuditTrailWriter>();

    await writer.WriteAsync(new AuditEntry
    {
        ResourceType = "Submission",
        ResourceId = submissionId,
        Action = "Canvas1PiiWrite",
        Actor = new AuditActor("lab", "Lab", ActorType: AuditActorType.System),
        Before = payloadJson,
        After = payloadJson,
        CorrelationId = correlationId,
        OccurredAt = DateTimeOffset.UtcNow
    });

    var rows = await ReadAuditTrailRowsAsync(sql, correlationId, submissionId);
    if (rows.Count == 0)
    {
        throw new InvalidOperationException("No audit rows found.");
    }

    var before = rows[0].BeforeJson ?? "";
    var after = rows[0].AfterJson ?? "";

    if (!before.Contains("[REDACTED]", StringComparison.OrdinalIgnoreCase) && !after.Contains("[REDACTED]", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Expected [REDACTED] token in stored audit JSON.");
    }
    if (before.Contains("user@example.com", StringComparison.OrdinalIgnoreCase) || after.Contains("user@example.com", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Email was not redacted in audit trail.");
    }
    if (before.Contains("555", StringComparison.OrdinalIgnoreCase) || after.Contains("555", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Phone was not redacted in audit trail.");
    }
});

await Run("eventbus_single_delivery", async () =>
{
    var queue = new ConcurrentQueue<TestEvent>();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddKsEventBus(bus =>
    {
        bus.Provider = EventBusProvider.InMemory;
        bus.UseOutbox = false;
    });
    services.AddSingleton(queue);
    services.AddConsumer<TestEvent, CapturingConsumer>("canvas1-events", "sub-1");
    var sp = services.BuildServiceProvider();

    var publisher = sp.GetRequiredService<IEventPublisher>();
    await publisher.PublishDirectAsync("canvas1-events", "TestEvent", new TestEvent(submissionId, "hello"));

    if (queue.Count != 1)
    {
        throw new InvalidOperationException($"Expected 1 event delivery, got {queue.Count}.");
    }
});

await Run("idempotency_blocks_duplicate", async () =>
{
    var redis = Environment.GetEnvironmentVariable("LAB_REDIS") ?? "localhost:6379";
    var queue = new ConcurrentQueue<TestEvent>();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(queue);
    services.AddKsEventBus(bus =>
    {
        bus.Provider = EventBusProvider.InMemory;
        bus.UseOutbox = false;
    });
    services.AddConsumer<TestEvent, CapturingConsumer>("canvas1-idem-events", "sub-1");
    services.AddKsIdempotency(opt =>
    {
        opt.Provider = IdempotencyProvider.Redis;
        opt.RedisConnectionString = redis;
    });

    var sp = services.BuildServiceProvider();
    var guard = sp.GetRequiredService<IIdempotencyGuard>();
    var publisher = sp.GetRequiredService<IEventPublisher>();

    var key = $"canvas1:{submissionId}:{correlationId}";

    async Task<bool> PublishOnceAsync()
    {
        var marked = await guard.TryMarkProcessedAsync(key, TimeSpan.FromMinutes(5));
        if (marked)
        {
            await publisher.PublishAsync(
                "canvas1-idem-events",
                "TestEvent",
                new TestEvent(submissionId, "idempotent"),
                new EventPublishOptions
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    CorrelationId = correlationId
                }
            );
        }

        return marked;
    }

    var first = await PublishOnceAsync();
    var second = await PublishOnceAsync();

    if (!first || second)
    {
        throw new InvalidOperationException($"Expected first=true second=false, got first={first} second={second}.");
    }

    if (queue.Count != 1)
    {
        throw new InvalidOperationException($"Expected consumer called exactly once; got {queue.Count}.");
    }
});

await Run("azurite_blob_roundtrip", async () =>
{
    var azurite = Environment.GetEnvironmentVariable("LAB_AZURITE_BLOB")
                  ?? "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey="
                  + "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
                  + "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddKsBlobStorage(opt =>
    {
        opt.Provider = BlobProvider.Azure;
        opt.ConnectionString = azurite;
    });
    var blob = services.BuildServiceProvider().GetRequiredService<IBlobStorageConnector>();

    var container = "canvas1";
    var blobPath = $"inputs/{submissionId}.bin";
    var bytes = RandomNumberGenerator.GetBytes(1024);
    await using var ms = new MemoryStream(bytes);

    var upload = await blob.UploadAsync(new BlobUploadRequest(container, blobPath, ms, "application/octet-stream"));
    await using var dl = await blob.DownloadAsync(upload.BlobPath);
    var downloaded = await ReadAllBytesAsync(dl.Content);

    var h1 = Convert.ToHexString(SHA256.HashData(bytes));
    var h2 = Convert.ToHexString(SHA256.HashData(downloaded));
    if (!h1.Equals(h2, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Blob roundtrip SHA256 mismatch.");
    }
});

await Run("audit_append_only_two_writes", async () =>
{
    await EnsureAuditTrailSchemaAsync(sql);

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddKsAuditTrail(options =>
    {
        options.Provider = AuditProvider.SqlServer;
        options.ConnectionString = sql;
        options.ServiceName = "lab-canvas-1";
        options.MaskPiiInBeforeAfter = false;
    });

    var writer = services.BuildServiceProvider().GetRequiredService<IAuditTrailWriter>();
    var actor = new AuditActor("lab", "Lab", ActorType: AuditActorType.System);

    var cid = Guid.NewGuid().ToString("N");
    await writer.WriteAsync(new AuditEntry
    {
        ResourceType = "Submission",
        ResourceId = submissionId,
        Action = "Canvas1AppendOnlyA",
        Actor = actor,
        CorrelationId = cid
    });
    await writer.WriteAsync(new AuditEntry
    {
        ResourceType = "Submission",
        ResourceId = submissionId,
        Action = "Canvas1AppendOnlyB",
        Actor = actor,
        CorrelationId = cid
    });

    var rows = await ReadAuditTrailRowsAsync(sql, cid, submissionId);
    if (rows.Count != 2)
    {
        throw new InvalidOperationException($"Expected exactly 2 audit rows, got {rows.Count}.");
    }
});

var output = JsonSerializer.Serialize(new
{
    submissionId,
    correlationId,
    assertions
}, new JsonSerializerOptions { WriteIndented = true });

Console.WriteLine(output);
return assertions.All(a => a.passed) ? 0 : 1;

void Record(string name, bool passed, string details)
{
    assertions.Add(new AssertionRecord(name, passed, details));
}

async Task Run(string name, Func<Task> action)
{
    try
    {
        await action();
        Record(name, true, "");
    }
    catch (Exception ex)
    {
        Record(name, false, ex.Message);
    }
}

static async Task EnsureAuditTrailSchemaAsync(string connectionString)
{
    var sql = """
              IF OBJECT_ID('dbo.audit_trail', 'U') IS NULL
              BEGIN
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
                      occurred_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
                  );
                  CREATE INDEX IX_audit_resource ON audit_trail (resource_type, resource_id, occurred_at DESC);
                  CREATE INDEX IX_audit_actor ON audit_trail (actor_user_id, occurred_at DESC);
                  CREATE INDEX IX_audit_occurred ON audit_trail (occurred_at DESC);
              END
              """;

    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

static async Task<List<(string? BeforeJson, string? AfterJson)>> ReadAuditTrailRowsAsync(
    string connectionString,
    string correlationId,
    string resourceId
)
{
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
                      SELECT TOP 10 before_json, after_json
                      FROM audit_trail
                      WHERE correlation_id = @cid AND resource_id = @rid
                      ORDER BY occurred_at DESC
                      """;
    cmd.Parameters.AddWithValue("@cid", correlationId);
    cmd.Parameters.AddWithValue("@rid", resourceId);

    await using var reader = await cmd.ExecuteReaderAsync();
    var list = new List<(string? BeforeJson, string? AfterJson)>();
    while (await reader.ReadAsync())
    {
        var before = reader.IsDBNull(0) ? null : reader.GetString(0);
        var after = reader.IsDBNull(1) ? null : reader.GetString(1);
        list.Add((before, after));
    }

    return list;
}

static async Task<byte[]> ReadAllBytesAsync(Stream stream)
{
    await using var ms = new MemoryStream();
    await stream.CopyToAsync(ms);
    return ms.ToArray();
}

sealed record AssertionRecord(string name, bool passed, string details);

sealed record TestEvent(string SubmissionId, string Message);

sealed class CapturingConsumer(ConcurrentQueue<TestEvent> queue) : KSquare.EventBus.Contracts.IEventConsumer<TestEvent>
{
    public Task ConsumeAsync(EventContext<TestEvent> context, CancellationToken ct = default)
    {
        queue.Enqueue(context.Payload);
        return Task.CompletedTask;
    }
}
