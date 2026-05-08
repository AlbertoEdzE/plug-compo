using Bogus;
using FluentAssertions;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Models;
using KSquare.Notifications.Configuration;
using KSquare.Notifications.Contracts;
using KSquare.Notifications.Channels;
using KSquare.Notifications.Database;
using KSquare.Notifications.Internal;
using KSquare.Notifications.Models;
using KSquare.PiiRedaction.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSquare.Notifications.Tests;

public sealed class NotificationDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_ShouldSendEmailAndPersistInApp_ForNormalPriority()
    {
        await using var db = CreateDb();
        var options = new NotificationOptions
        {
            EnableEmail = true,
            EnableInApp = true,
            DeduplicationWindow = TimeSpan.FromMinutes(30)
        };

        var emailSender = new FakeEmailSender();
        var pii = new PassthroughPiiRedactor();

        var channels = new INotificationChannel[]
        {
            new EmailNotificationChannel(
                emailSender,
                new KSquare.EmailSend.Configuration.EmailSendOptions(),
                NullLogger<EmailNotificationChannel>.Instance,
                pii
            ),
            new InAppNotificationChannel(db, NullLogger<InAppNotificationChannel>.Instance, pii)
        };

        var dispatcher = new NotificationDispatcher(
            options,
            channels,
            new DedupService(options, db),
            db,
            NullLogger<NotificationDispatcher>.Instance,
            pii
        );

        var request = SynthesizeRequest(priority: NotificationPriority.Normal);
        await dispatcher.DispatchAsync(request);

        emailSender.Sent.Should().HaveCount(1);
        (await db.InAppNotifications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DispatchAsync_ShouldSkipDuplicateWithinWindow()
    {
        await using var db = CreateDb();
        var options = new NotificationOptions
        {
            EnableEmail = true,
            EnableInApp = true,
            DeduplicationWindow = TimeSpan.FromHours(24)
        };

        var emailSender = new FakeEmailSender();
        var pii = new PassthroughPiiRedactor();

        var channels = new INotificationChannel[]
        {
            new EmailNotificationChannel(
                emailSender,
                new KSquare.EmailSend.Configuration.EmailSendOptions(),
                NullLogger<EmailNotificationChannel>.Instance,
                pii
            ),
            new InAppNotificationChannel(db, NullLogger<InAppNotificationChannel>.Instance, pii)
        };

        var dispatcher = new NotificationDispatcher(
            options,
            channels,
            new DedupService(options, db),
            db,
            NullLogger<NotificationDispatcher>.Instance,
            pii
        );

        var request = SynthesizeRequest(priority: NotificationPriority.Normal);
        await dispatcher.DispatchAsync(request);
        await dispatcher.DispatchAsync(request);

        emailSender.Sent.Should().HaveCount(1);
        (await db.InAppNotifications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DispatchAsync_EmailFailure_ShouldNotPreventInApp()
    {
        await using var db = CreateDb();
        var options = new NotificationOptions
        {
            EnableEmail = true,
            EnableInApp = true,
            DeduplicationWindow = TimeSpan.FromMinutes(30)
        };

        var emailSender = new ThrowingEmailSender();
        var pii = new PassthroughPiiRedactor();

        var channels = new INotificationChannel[]
        {
            new EmailNotificationChannel(
                emailSender,
                new KSquare.EmailSend.Configuration.EmailSendOptions(),
                NullLogger<EmailNotificationChannel>.Instance,
                pii
            ),
            new InAppNotificationChannel(db, NullLogger<InAppNotificationChannel>.Instance, pii)
        };

        var dispatcher = new NotificationDispatcher(
            options,
            channels,
            new DedupService(options, db),
            db,
            NullLogger<NotificationDispatcher>.Instance,
            pii
        );

        var request = SynthesizeRequest(priority: NotificationPriority.Normal);
        await dispatcher.DispatchAsync(request);

        (await db.InAppNotifications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetInAppAsync_ShouldReturnNewestFirst()
    {
        await using var db = CreateDb();
        var options = new NotificationOptions
        {
            EnableEmail = false,
            EnableInApp = true,
            DeduplicationWindow = TimeSpan.Zero
        };

        db.InAppNotifications.AddRange(
            new InAppNotificationRecord
            {
                NotificationId = Guid.NewGuid(),
                UserId = "u1",
                EventType = "t",
                Title = "older",
                Body = "b",
                ResourceType = "Submission",
                ResourceId = "r1",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new InAppNotificationRecord
            {
                NotificationId = Guid.NewGuid(),
                UserId = "u1",
                EventType = "t",
                Title = "newer",
                Body = "b",
                ResourceType = "Submission",
                ResourceId = "r1",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        );
        await db.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(
            options,
            Array.Empty<INotificationChannel>(),
            new DedupService(options, db),
            db,
            NullLogger<NotificationDispatcher>.Instance,
            new PassthroughPiiRedactor()
        );

        var titles = new List<string>();
        await foreach (var n in dispatcher.GetInAppAsync("u1", limit: 10))
        {
            titles.Add(n.Title);
        }

        titles.Should().Equal("newer", "older");
    }

    [Fact]
    public async Task MarkReadAsync_ShouldSetIsReadAndReadAt()
    {
        await using var db = CreateDb();
        var options = new NotificationOptions
        {
            EnableEmail = false,
            EnableInApp = true,
            DeduplicationWindow = TimeSpan.Zero
        };

        var id = Guid.NewGuid();
        db.InAppNotifications.Add(new InAppNotificationRecord
        {
            NotificationId = id,
            UserId = "u1",
            EventType = "t",
            Title = "title",
            Body = "body",
            ResourceType = "Submission",
            ResourceId = "r1",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(
            options,
            Array.Empty<INotificationChannel>(),
            new DedupService(options, db),
            db,
            NullLogger<NotificationDispatcher>.Instance,
            new PassthroughPiiRedactor()
        );

        await dispatcher.MarkReadAsync("u1", id);

        var row = await db.InAppNotifications.SingleAsync(x => x.NotificationId == id);
        row.IsRead.Should().BeTrue();
        row.ReadAt.Should().NotBeNull();
    }

    private static NotificationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new NotificationDbContext(options);
    }

    private static NotificationRequest SynthesizeRequest(NotificationPriority priority)
    {
        var faker = new Faker();

        return new NotificationRequest
        {
            EventType = "submission.assigned",
            Title = faker.Lorem.Sentence(3),
            Body = faker.Lorem.Paragraph(),
            ResourceType = "Submission",
            ResourceId = faker.Random.Guid().ToString(),
            ActionUrl = faker.Internet.Url(),
            CorrelationId = faker.Random.Guid().ToString(),
            Priority = priority,
            Recipients =
            [
                new NotificationRecipient(
                    UserId: faker.Random.Guid().ToString("N"),
                    Email: faker.Internet.Email(),
                    DisplayName: faker.Name.FullName()
                )
            ]
        };
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _sent = new();
        public IReadOnlyList<EmailMessage> Sent => _sent;

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            _sent.Add(message);
            return Task.FromResult(new EmailSendResult(true, "id", null, DateTimeOffset.UtcNow));
        }

        public Task<EmailSendResult> SendTemplatedAsync<TModel>(
            string templateName,
            TModel model,
            EmailAddress to,
            string? subject = null,
            IReadOnlyList<EmailAttachmentRef>? attachments = null,
            CancellationToken ct = default
        )
            where TModel : class
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default) => throw new InvalidOperationException("boom");

        public Task<EmailSendResult> SendTemplatedAsync<TModel>(
            string templateName,
            TModel model,
            EmailAddress to,
            string? subject = null,
            IReadOnlyList<EmailAttachmentRef>? attachments = null,
            CancellationToken ct = default
        )
            where TModel : class
        {
            throw new NotSupportedException();
        }
    }

    private sealed class PassthroughPiiRedactor : IPiiRedactor
    {
        public string RedactJson(string json) => json;
        public string RedactValue(string value) => value;
        public bool IsPiiField(string fieldName) => false;
    }
}
