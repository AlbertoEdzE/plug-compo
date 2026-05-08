using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Extensions;
using KSquare.Correlation.Models;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Contracts;
using KSquare.EmailIngestion.Models;
using KSquare.EmailIngestion.Extensions;
using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Extensions;
using KSquare.EmailSend.Models;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Extensions;
using KSquare.Notifications.Configuration;
using KSquare.Notifications.Contracts;
using KSquare.Notifications.Extensions;
using KSquare.Notifications.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

var assertions = new List<AssertionRecord>();

var seed = Environment.GetEnvironmentVariable("CANVAS_SEED") ?? "42";
var submissionId = $"SUB-{Random.Shared.Next(1000, 9999)}";
var correlationId = Guid.NewGuid().ToString("N");

Record("synthesize_canvas2_ids", !string.IsNullOrWhiteSpace(submissionId) && !string.IsNullOrWhiteSpace(correlationId), $"{submissionId} / {correlationId} / seed={seed}");

var sql = Environment.GetEnvironmentVariable("LAB_SQL_CONNECTION")
          ?? "Server=localhost,1433;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";
var redis = Environment.GetEnvironmentVariable("LAB_REDIS") ?? "localhost:6379";
var wiremock = Environment.GetEnvironmentVariable("LAB_WIREMOCK") ?? "http://localhost:8080";
var graphBase = Environment.GetEnvironmentVariable("LAB_GRAPH_BASE") ?? wiremock;
var azurite = Environment.GetEnvironmentVariable("LAB_AZURITE_BLOB")
              ?? "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey="
              + "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
              + "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

await Run("wiremock_stub_graph_and_sendgrid", async () =>
{
    using var http = new HttpClient { BaseAddress = new Uri(wiremock) };
    await http.DeleteAsync("/__admin/requests");

    var mailbox = "submissions@company.test";
    var graphMessageId = "graph-msg-1";
    var received = DateTimeOffset.UtcNow.ToString("O");

    var rawMime = BuildMimeWithTwoAttachments(
        mailbox,
        messageIdHeader: "<canvas2-1@company.test>",
        subject: "New submission - Canvas 2",
        attachment1FileName: "acord125.pdf",
        attachment2FileName: "loss_run.xlsx"
    );

    var listMapping = new
    {
        request = new { method = "GET", urlPathPattern = $"/v1.0/users/{mailbox}/mailFolders/Inbox/messages" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new
            {
                value = new[]
                {
                    new { id = graphMessageId, receivedDateTime = received }
                }
            }
        }
    };

    var rawMapping = new
    {
        request = new { method = "GET", urlPath = $"/v1.0/users/{mailbox}/messages/{graphMessageId}/$value" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "message/rfc822" },
            body = rawMime
        }
    };

    var patchMapping = new
    {
        request = new { method = "PATCH", urlPath = $"/v1.0/users/{mailbox}/messages/{graphMessageId}" },
        response = new { status = 200, headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" }, body = "{}" }
    };

    var moveMapping = new
    {
        request = new { method = "POST", urlPath = $"/v1.0/users/{mailbox}/messages/{graphMessageId}/move" },
        response = new { status = 200, headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" }, body = "{}" }
    };

    var sendgridMapping = new
    {
        request = new { method = "POST", urlPath = "/v3/mail/send" },
        response = new
        {
            status = 202,
            headers = new Dictionary<string, string> { ["X-Message-Id"] = "wiremock-msg-1" },
            body = ""
        }
    };

    await http.PostAsJsonAsync("/__admin/mappings", listMapping);
    await http.PostAsJsonAsync("/__admin/mappings", rawMapping);
    await http.PostAsJsonAsync("/__admin/mappings", patchMapping);
    await http.PostAsJsonAsync("/__admin/mappings", moveMapping);
    await http.PostAsJsonAsync("/__admin/mappings", sendgridMapping);
});

await Run("email_ingestion_publishes_one_event_and_stores_two_attachments", async () =>
{
    var queue = new ConcurrentQueue<EmailReceivedEvent>();

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(queue);

    services.AddKsEventBus(bus =>
    {
        bus.Provider = EventBusProvider.InMemory;
        bus.UseOutbox = false;
    });
    services.AddConsumer<EmailReceivedEvent, CapturingEmailReceivedConsumer>("email-intake-events", "canvas2-sub");

    services.AddKsBlobStorage(blob =>
    {
        blob.Provider = BlobProvider.Azure;
        blob.ConnectionString = azurite;
    });

    services.AddKsIdempotency(opt =>
    {
        opt.Provider = IdempotencyProvider.Redis;
        opt.RedisConnectionString = redis;
    });

    services.AddKsEmailIngestion(opt =>
    {
        opt.Provider = EmailIngestionProvider.HttpGraphStub;
        opt.GraphApiBaseUrl = graphBase;
        opt.GraphAuthToken = "test-token";
        opt.MailboxAddress = "submissions@company.test";
        opt.InboxFolderName = "Inbox";
        opt.ProcessedFolderName = "Processed";
        opt.EventTopic = "email-intake-events";
        opt.AttachmentContainerName = "email-attachments";
        opt.MaxEmailsPerBatch = 1;
        opt.DuplicateDetectionWindow = TimeSpan.FromDays(3);
    });

    var sp = services.BuildServiceProvider();
    var connector = sp.GetRequiredService<IEmailIngestionConnector>();
    var blob = sp.GetRequiredService<IBlobStorageConnector>();

    var r1 = await connector.PollAndProcessAsync();
    var r2 = await connector.PollAndProcessAsync();

    if (queue.Count != 1)
    {
        throw new InvalidOperationException($"Expected exactly one EmailReceivedEvent, got {queue.Count}.");
    }

    var evt = queue.Single();
    if (evt.AttachmentCount != 2 || evt.AttachmentBlobPaths.Count != 2)
    {
        throw new InvalidOperationException($"Expected 2 attachments, got {evt.AttachmentCount}.");
    }

    foreach (var path in evt.AttachmentBlobPaths)
    {
        var exists = await blob.ExistsAsync(path);
        if (!exists)
        {
            throw new InvalidOperationException($"Attachment blob not found: {path}");
        }
    }
});

await Run("email_send_template_renders_variable_and_hits_wiremock_sendgrid", async () =>
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddKsBlobStorage(blob =>
    {
        blob.Provider = BlobProvider.Azure;
        blob.ConnectionString = azurite;
    });

    services.AddKsEmailSend(opt =>
    {
        opt.Provider = EmailSendProvider.SendGrid;
        opt.SendGridApiKey = "wiremock-key";
        opt.SendGridBaseUrl = wiremock;
        opt.TemplateSource = EmailTemplateSource.EmbeddedResource;
        opt.MaxRetryAttempts = 1;
    });

    var sender = services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    var to = new EmailAddress("broker@broker.test", "Broker");

    var model = new
    {
        SubmissionNumber = "SUB-2042",
        BrokerName = "Jane Smith",
        OldStatus = "Draft",
        NewStatus = "Submitted",
        PortalUrl = "https://uw.company.test/submissions/2042"
    };

    var result = await sender.SendTemplatedAsync("submission-status-changed", model, to);
    if (!result.Success)
    {
        throw new InvalidOperationException($"SendTemplatedAsync failed: {result.Error}");
    }

    using var http = new HttpClient { BaseAddress = new Uri(wiremock) };
    var journal = await http.GetStringAsync("/__admin/requests");
    if (!journal.Contains("/v3/mail/send", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("WireMock request journal does not contain a SendGrid POST.");
    }

    if (!journal.Contains("Jane Smith", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Rendered email did not include expected variable value (BrokerName).");
    }

    if (journal.Contains("{{ BrokerName }}", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Template placeholder was not rendered (found '{{ BrokerName }}').");
    }
});

await Run("notification_dispatch_persists_in_sql_and_dedups_to_one_row", async () =>
{
    await EnsureNotificationSchemaAsync(sql);

    var services = new ServiceCollection();
    services.AddLogging();

    services.AddKsBlobStorage(blob =>
    {
        blob.Provider = BlobProvider.Azure;
        blob.ConnectionString = azurite;
    });
    services.AddKsEmailSend(opt =>
    {
        opt.Provider = EmailSendProvider.SendGrid;
        opt.SendGridApiKey = "wiremock-key";
        opt.SendGridBaseUrl = wiremock;
        opt.TemplateSource = EmailTemplateSource.EmbeddedResource;
        opt.MaxRetryAttempts = 1;
    });

    services.AddKsNotifications(opt =>
    {
        opt.EnableEmail = true;
        opt.EnableInApp = true;
        opt.ConnectionString = sql;
        opt.DeduplicationWindow = TimeSpan.FromMinutes(5);
    });

    var dispatcher = services.BuildServiceProvider().GetRequiredService<INotificationDispatcher>();

    var userId = $"uw-{Guid.NewGuid():N}";
    var request = new NotificationRequest
    {
        EventType = "submission.assigned",
        Title = "New Submission Assigned",
        Body = $"Submission {submissionId} has been assigned.",
        ResourceType = "Submission",
        ResourceId = submissionId,
        ActionUrl = "https://uw.company.test/submissions/x",
        CorrelationId = correlationId,
        Priority = NotificationPriority.Normal,
        Recipients =
        [
            new NotificationRecipient(
                UserId: userId,
                Email: "uw@company.test",
                DisplayName: "Underwriter"
            )
        ]
    };

    await dispatcher.DispatchAsync(request);
    await dispatcher.DispatchAsync(request);

    var count = await CountInAppNotificationsAsync(sql, userId, request.EventType, request.ResourceId);
    if (count != 1)
    {
        throw new InvalidOperationException($"Expected exactly 1 in_app_notifications row after dedup, got {count}.");
    }
});

var output = JsonSerializer.Serialize(new { submissionId, correlationId, assertions }, new JsonSerializerOptions { WriteIndented = true });
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

static string BuildMimeWithTwoAttachments(
    string mailbox,
    string messageIdHeader,
    string subject,
    string attachment1FileName,
    string attachment2FileName
)
{
    var boundary = "----=_Part_654321_210987654.1700000000000";
    var attachment1Payload = "SGVsbG8gQUNPUkQgMTI1";
    var attachment2Payload = "SGVsbG8gTG9zcyBSdW4=";

    return
        $"Message-ID: {messageIdHeader}\r\n" +
        $"Date: Tue, 01 Sep 2026 12:00:00 +0000\r\n" +
        $"From: Broker <broker@broker.test>\r\n" +
        $"To: {mailbox}\r\n" +
        $"Subject: {subject}\r\n" +
        $"MIME-Version: 1.0\r\n" +
        $"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n" +
        $"\r\n" +
        $"--{boundary}\r\n" +
        $"Content-Type: text/plain; charset=\"utf-8\"\r\n" +
        $"\r\n" +
        $"Please find the submission attached.\r\n" +
        $"\r\n" +
        $"--{boundary}\r\n" +
        $"Content-Type: application/pdf\r\n" +
        $"Content-Disposition: attachment; filename=\"{attachment1FileName}\"\r\n" +
        $"Content-Transfer-Encoding: base64\r\n" +
        $"\r\n" +
        $"{attachment1Payload}\r\n" +
        $"\r\n" +
        $"--{boundary}\r\n" +
        $"Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\r\n" +
        $"Content-Disposition: attachment; filename=\"{attachment2FileName}\"\r\n" +
        $"Content-Transfer-Encoding: base64\r\n" +
        $"\r\n" +
        $"{attachment2Payload}\r\n" +
        $"\r\n" +
        $"--{boundary}--\r\n";
}

static async Task EnsureNotificationSchemaAsync(string connectionString)
{
    var sql = """
              IF OBJECT_ID('dbo.in_app_notifications', 'U') IS NULL
              BEGIN
                  CREATE TABLE in_app_notifications (
                      notification_id   UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
                      user_id           NVARCHAR(500) NOT NULL,
                      event_type        NVARCHAR(200) NOT NULL,
                      title             NVARCHAR(500) NOT NULL,
                      body              NVARCHAR(MAX) NOT NULL,
                      action_url        NVARCHAR(1000) NULL,
                      resource_type     NVARCHAR(100) NOT NULL,
                      resource_id       NVARCHAR(500) NOT NULL,
                      is_read           BIT NOT NULL DEFAULT 0,
                      created_at        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                      read_at           DATETIMEOFFSET NULL
                  );
                  CREATE INDEX IX_notif_user_unread ON in_app_notifications (user_id, is_read, created_at DESC);
                  CREATE INDEX IX_notif_created ON in_app_notifications (created_at DESC);
              END

              IF OBJECT_ID('dbo.notification_dedup', 'U') IS NULL
              BEGIN
                  CREATE TABLE notification_dedup (
                      dedup_key       NVARCHAR(500) NOT NULL PRIMARY KEY,
                      created_at      DATETIMEOFFSET NOT NULL,
                      expires_at      DATETIMEOFFSET NOT NULL
                  );
              END
              """;

    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

static async Task<int> CountInAppNotificationsAsync(string connectionString, string userId, string eventType, string resourceId)
{
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
                      SELECT COUNT(1)
                      FROM in_app_notifications
                      WHERE user_id = @user_id AND event_type = @event_type AND resource_id = @resource_id;
                      """;
    cmd.Parameters.AddWithValue("@user_id", userId);
    cmd.Parameters.AddWithValue("@event_type", eventType);
    cmd.Parameters.AddWithValue("@resource_id", resourceId);
    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(result);
}

sealed record AssertionRecord(string name, bool passed, string details);

sealed class CapturingEmailReceivedConsumer(ConcurrentQueue<EmailReceivedEvent> queue) : IEventConsumer<EmailReceivedEvent>
{
    public Task ConsumeAsync(EventContext<EmailReceivedEvent> context, CancellationToken ct = default)
    {
        queue.Enqueue(context.Payload);
        return Task.CompletedTask;
    }
}
