using FluentAssertions;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.BlobStorage.Providers;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Internal;
using KSquare.EmailIngestion.Models;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Models;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace KSquare.EmailIngestion.Tests;

public sealed class EmailIngestionConnectorTests
{
    [Fact]
    public async Task PollAndProcessAsync_ShouldStoreBlobs_PublishEvent_AndMoveMessage()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender Name", "sender@example.com"));
        message.To.Add(new MailboxAddress(string.Empty, "to@example.com"));
        message.Subject = "New submission - test";
        message.Date = now;

        var builder = new BodyBuilder
        {
            TextBody = "This is a new submission."
        };
        builder.Attachments.Add("note.txt", new MemoryStream(new byte[] { 1, 2, 3 }), new ContentType("text", "plain"));
        message.Body = builder.ToMessageBody();

        byte[] raw;
        using (var ms = new MemoryStream())
        {
            message.WriteTo(ms);
            raw = ms.ToArray();
        }

        var options = new EmailIngestionOptions
        {
            EventTopic = "email-intake-events",
            AttachmentContainerName = "email-attachments",
            AttachmentPathTemplate = "incoming/{year}/{month}/{day}/{correlationId}/{fileName}",
            DuplicateDetectionWindow = TimeSpan.FromMinutes(10)
        };

        var tempRoot = Path.Combine(Path.GetTempPath(), "ks-email-ingestion-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));

        IBlobStorageConnector blob = new LocalFileSystemConnector(
            new OptionsWrapper<BlobStorageOptions>(new BlobStorageOptions
            {
                Provider = BlobProvider.LocalFileSystem,
                LocalRootPath = tempRoot,
                MaxUploadSizeBytes = 100 * 1024 * 1024
            }),
            loggerFactory.CreateLogger<LocalFileSystemConnector>()
        );

        var source = new FakeEmailSource(new[]
        {
            new FetchedEmail("source-1", raw, now)
        });

        var mover = new FakeEmailMover();
        var parser = new MimeEmailParser();

        var idempotency = new InMemoryIdempotencyGuard(new IdempotencyOptions
        {
            Provider = IdempotencyProvider.InMemory,
            DefaultHttpKeyTtl = TimeSpan.FromMinutes(10)
        });
        var duplicates = new IdempotencyDuplicateDetector(options, idempotency);

        var attachmentStore = new BlobAttachmentStore(options, blob);
        var events = new RecordingEventPublisher();
        var logger = loggerFactory.CreateLogger<EmailIngestionConnector>();

        var connector = new EmailIngestionConnector(
            options,
            source,
            mover,
            parser,
            duplicates,
            attachmentStore,
            events,
            logger
        );

        var result = await connector.PollAndProcessAsync();

        result.TotalFetched.Should().Be(1);
        result.NewlyProcessed.Should().Be(1);
        result.DuplicatesSkipped.Should().Be(0);
        result.Errors.Should().Be(0);

        mover.MovedSourceMessageIds.Should().ContainSingle().Which.Should().Be("source-1");

        events.Published.Should().ContainSingle(e => e.EventType == "email.received" && e.Topic == "email-intake-events");
        var published = events.Published.Single();

        var received = published.Payload.Should().BeOfType<EmailReceivedEvent>().Subject;
        received.CorrelationId.Should().NotBeNullOrWhiteSpace();
        received.MessageId.Should().NotBeNullOrWhiteSpace();
        received.Subject.Should().Be("New submission - test");
        received.DetectedIntentHint.Should().Be("NewSubmission");
        received.AttachmentCount.Should().Be(1);
        received.AttachmentBlobPaths.Should().HaveCount(1);

        received.RawEmailBlobPath.Should().Contain(received.CorrelationId);
        received.RawEmailBlobPath.Should().EndWith($"/{received.CorrelationId}/raw.eml");
        received.AttachmentBlobPaths[0].Should().Contain(received.CorrelationId);
        received.AttachmentBlobPaths[0].Should().EndWith($"/{received.CorrelationId}/note.txt");

        (await blob.ExistsAsync(received.RawEmailBlobPath)).Should().BeTrue();
        (await blob.ExistsAsync(received.AttachmentBlobPaths[0])).Should().BeTrue();

        var secondResult = await connector.PollAndProcessAsync();
        secondResult.TotalFetched.Should().Be(1);
        secondResult.NewlyProcessed.Should().Be(0);
        secondResult.DuplicatesSkipped.Should().Be(1);
        secondResult.Errors.Should().Be(0);
    }

    private sealed class FakeEmailSource(IReadOnlyList<FetchedEmail> emails) : IEmailSource
    {
        public Task<IReadOnlyList<FetchedEmail>> FetchUnreadAsync(CancellationToken ct = default) => Task.FromResult(emails);
    }

    private sealed class FakeEmailMover : IEmailMover
    {
        private readonly List<string> _moved = new();
        public IReadOnlyList<string> MovedSourceMessageIds => _moved;

        public Task MarkReadAndMoveToProcessedAsync(string sourceMessageId, CancellationToken ct = default)
        {
            _moved.Add(sourceMessageId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        private readonly List<PublishedEvent> _published = new();
        public IReadOnlyList<PublishedEvent> Published => _published;

        public Task PublishAsync<T>(string topic, string eventType, T payload, EventPublishOptions? options = null, CancellationToken ct = default)
            where T : class
        {
            _published.Add(new PublishedEvent(topic, eventType, payload, options));
            return Task.CompletedTask;
        }

        public Task PublishDirectAsync<T>(string topic, string eventType, T payload, CancellationToken ct = default)
            where T : class
        {
            _published.Add(new PublishedEvent(topic, eventType, payload, null));
            return Task.CompletedTask;
        }
    }

    private sealed record PublishedEvent(string Topic, string EventType, object Payload, EventPublishOptions? Options);
}
