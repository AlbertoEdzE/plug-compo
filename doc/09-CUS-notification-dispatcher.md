# Component 09 — Notification Dispatcher

**Library**: `KSquare.Notifications`  
**Layer**: Communication  
**Default Channels**: Email (via KSquare.EmailSend), In-App Bell  
**Future Channels**: SMS (Twilio), Microsoft Teams webhook  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Multiple workbench events trigger user-facing notifications:
- Submission assigned → notify underwriter
- Referral opened → notify senior underwriter
- Quote expired (T-3 days) → notify broker
- Document requested → notify broker
- Bind approval → notify underwriter + broker

Without a dispatcher library, each service decides independently which channels to use, duplicates
channel logic, and has no unified notification preference model. When a new channel (Teams) is added,
every service must be updated.

The complexity justifying a library:
- Multi-channel routing from a single `Notify(...)` call
- Per-user, per-notification-type channel preferences (user table: prefers email + in-app, not SMS)
- In-app notification persistence (stored in SQL, polled by frontend via `/api/me/notifications`)
- Deduplication: same event + same user within a window = one notification
- Notification template decoupling from channel implementation

---

## Interface Contract

```csharp
namespace KSquare.Notifications.Contracts;

public interface INotificationDispatcher
{
    // Dispatch a notification to one or more recipients across all configured channels.
    Task DispatchAsync(NotificationRequest request, CancellationToken ct = default);

    // Mark one or all in-app notifications as read for a user.
    Task MarkReadAsync(string userId, Guid? notificationId = null, CancellationToken ct = default);

    // Fetch in-app notifications for a user (for polling endpoint).
    IAsyncEnumerable<InAppNotification> GetInAppAsync(string userId, int limit = 50, CancellationToken ct = default);
}

public interface INotificationChannel
{
    string ChannelName { get; }   // "email", "inapp", "sms", "teams"
    Task SendAsync(NotificationRequest request, NotificationRecipient recipient, CancellationToken ct);
}
```

---

## Models

```csharp
namespace KSquare.Notifications.Models;

public record NotificationRequest
{
    public required string EventType { get; init; }        // "submission.assigned", "referral.opened"
    public required IReadOnlyList<NotificationRecipient> Recipients { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }             // plain text body
    public string? HtmlBody { get; init; }                 // if null, plain body used for email
    public required string ResourceType { get; init; }     // "Submission", "Referral"
    public required string ResourceId { get; init; }
    public string? ActionUrl { get; init; }                // deep link for in-app + email CTA
    public string? CorrelationId { get; init; }
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
}

public record NotificationRecipient(
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string>? OverrideChannels = null  // if set, bypass preferences
);

public enum NotificationPriority { Low, Normal, High, Critical }

// Persisted in-app notification record
public record InAppNotification
{
    public Guid NotificationId { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required string EventType { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? ActionUrl { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public bool IsRead { get; set; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
```

---

## Configuration

```csharp
public class NotificationOptions
{
    // Which channels are active
    public bool EnableEmail { get; set; } = true;
    public bool EnableInApp { get; set; } = true;
    public bool EnableSms { get; set; } = false;
    public bool EnableTeams { get; set; } = false;

    // Default channels for each notification priority (if user has no preference)
    public IList<string> DefaultChannelsNormal { get; set; } = ["inapp", "email"];
    public IList<string> DefaultChannelsCritical { get; set; } = ["inapp", "email", "sms"];

    // In-app notification retention
    public int InAppRetentionDays { get; set; } = 30;

    // Deduplication window
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(5);

    // SQL connection for in-app persistence
    public string? ConnectionString { get; set; }
}
```

---

## DI Registration

```csharp
builder.Services.AddKsNotifications(options =>
{
    builder.Configuration.GetSection("KSquare:Notifications").Bind(options);
    options.ConnectionString = builder.Configuration.GetConnectionString("UwDb");
    options.EnableSms = false;
})
// Requires KSquare.EmailSend to be registered for the email channel.
;
```

---

## SQL Schema

```sql
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
    read_at           DATETIMEOFFSET NULL,
    INDEX IX_notif_user_unread (user_id, is_read, created_at DESC),
    INDEX IX_notif_created     (created_at DESC)
);

CREATE TABLE notification_dedup (
    dedup_key       NVARCHAR(500) NOT NULL PRIMARY KEY,
    created_at      DATETIMEOFFSET NOT NULL,
    expires_at      DATETIMEOFFSET NOT NULL
);
```

---

## Usage Examples

```csharp
// Notify underwriter of assignment
await dispatcher.DispatchAsync(new NotificationRequest
{
    EventType = "submission.assigned",
    Recipients = [new NotificationRecipient(
        UserId: underwriter.UserId,
        Email: underwriter.Email,
        DisplayName: underwriter.DisplayName
    )],
    Title = "New Submission Assigned",
    Body = $"Submission {submission.SubmissionNumber} from {broker.Name} has been assigned to you.",
    ResourceType = "Submission",
    ResourceId = submission.SubmissionId.ToString(),
    ActionUrl = $"https://uw.company.com/submissions/{submission.SubmissionId}",
    CorrelationId = correlationId
});

// Fetch unread in-app notifications (used by GET /api/me/notifications)
await foreach (var notif in dispatcher.GetInAppAsync(userId, limit: 20))
{
    // stream to response
}

// Mark single notification as read
await dispatcher.MarkReadAsync(userId, notificationId: notifId);
```

---

## Dispatch Flow

```
1. NotificationDispatcher.DispatchAsync(request)
   a. Compute dedup key = SHA256(eventType + resourceId + userId + floor(now, window))
   b. Check notification_dedup table — skip if key exists
   c. For each recipient:
      - Load channel preferences (from user preferences store or use defaults)
      - Apply override channels if set on recipient
      - For each active channel: call INotificationChannel.SendAsync(request, recipient)
   d. Insert dedup key with expiry = now + DeduplicationWindow

2. EmailNotificationChannel (wraps IEmailSender):
   - Use HtmlBody if set, otherwise wrap Body in minimal HTML
   - Send via emailSender.SendAsync(...)

3. InAppNotificationChannel:
   - INSERT into in_app_notifications
   - No push needed — frontend polls GET /api/me/notifications

4. Future SmsNotificationChannel:
   - Call Twilio API
   - Only for Critical priority or explicit override

5. Future TeamsNotificationChannel:
   - POST to Teams incoming webhook URL from per-user configuration
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Email channel fails | Log error; continue with other channels; do not fail entire dispatch |
| In-app DB unavailable | Log error; continue with email channel |
| Duplicate within window | Skip silently; log at Debug level |
| Recipient has no email | Skip email channel; continue with in-app |
| Unknown channel name in preferences | Log warning; fall back to default channels |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.Notifications at path: shared/KSquare.Notifications/

This library dispatches notifications across multiple channels (email, in-app, future SMS/Teams)
from a single DispatchAsync call. It persists in-app notifications to SQL.

Project structure:
  shared/KSquare.Notifications/
  ├── KSquare.Notifications.csproj
  ├── Contracts/
  │   ├── INotificationDispatcher.cs
  │   └── INotificationChannel.cs
  ├── Models/
  │   ├── NotificationRequest.cs
  │   ├── NotificationRecipient.cs
  │   ├── InAppNotification.cs
  │   └── NotificationPriority.cs (enum)
  ├── Configuration/
  │   └── NotificationOptions.cs
  ├── Channels/
  │   ├── EmailNotificationChannel.cs     ← wraps IEmailSender
  │   └── InAppNotificationChannel.cs     ← inserts to SQL
  ├── Database/
  │   ├── NotificationDbContext.cs
  │   └── Migrations/
  ├── Internal/
  │   └── DedupService.cs                 ← deduplication using notification_dedup table
  └── Extensions/
      └── ServiceCollectionExtensions.cs

NotificationDispatcher:
  - Inject IEnumerable<INotificationChannel> (all registered channels)
  - Inject DedupService
  - For each recipient:
    - Check dedup (skip if duplicate)
    - Fan out to each channel's SendAsync; catch and log per-channel exceptions (never rethrow)
  - Mark dedup after all channels processed

EmailNotificationChannel:
  - Inject IEmailSender (from KSquare.EmailSend)
  - Build EmailMessage from NotificationRequest
  - Map Priority.Critical → add "X-Priority: 1" header
  - Return without throwing on failure

InAppNotificationChannel:
  - Inject NotificationDbContext
  - Map NotificationRequest + NotificationRecipient → InAppNotification entity
  - INSERT into in_app_notifications

DedupService:
  - Compute SHA256 key from: eventType + resourceId + userId + RoundedTimeWindow
  - TryInsertAsync: INSERT into notification_dedup WHERE key NOT EXISTS; returns bool

NotificationDbContext:
  - DbSet<InAppNotificationRecord> mapped to in_app_notifications
  - DbSet<NotificationDedupRecord> mapped to notification_dedup
  - Include EF Core migration

ServiceCollectionExtensions:
  AddKsNotifications(Action<NotificationOptions>):
  - Registers INotificationDispatcher
  - Registers EmailNotificationChannel and InAppNotificationChannel as INotificationChannel
  - Registers NotificationDbContext
  - Checks IEmailSender is registered (throws clear error if not)

NuGet packages:
  - Microsoft.EntityFrameworkCore.SqlServer 8.x
  - System.Security.Cryptography (built-in for SHA256)

Tests at shared/KSquare.Notifications.Tests/:
  - DispatchAsync calls both channels for a normal priority notification
  - Duplicate within deduplication window is skipped (second call does not call channels)
  - Email channel failure does not prevent in-app channel from running
  - GetInAppAsync returns inserted notifications ordered by created_at DESC
  - MarkReadAsync sets IsRead = true and ReadAt timestamp
  Use xUnit + Moq + FluentAssertions + InMemory EF provider.
```
