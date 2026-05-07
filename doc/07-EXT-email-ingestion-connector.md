# Component 07 — Email Ingestion Connector

**Library**: `KSquare.EmailIngestion`  
**Layer**: Communication  
**Default Provider**: Microsoft Graph API (Exchange/O365 mailbox)  
**Alternate Providers**: IMAP, SendGrid Inbound Parse webhook  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Email ingestion is one of the most complex non-domain capabilities in the workbench:
- Reading from a shared mailbox via Microsoft Graph (OAuth 2.0 app permissions, delta query for efficiency)
- Parsing raw email: subject, body (text/HTML), sender, reply-to, all attachment MIME parts
- Storing raw email body + each attachment separately in Blob Storage
- Duplicate detection: same sender + same subject + same day = likely duplicate
- Routing classification: is this a new submission? An update? Junk? Needs human review?
- Publishing structured `EmailReceived` event to Service Bus for downstream processors

This is 400+ lines of non-trivial code that would otherwise be reimplemented for Claims,
Renewals, and any future email-driven workflow.

---

## Interface Contract

```csharp
namespace KSquare.EmailIngestion.Contracts;

public interface IEmailIngestionConnector
{
    // Pull-based: fetch new emails from the mailbox, process each, publish events.
    // Called by a HostedService on a schedule (e.g. every 30 seconds).
    Task<EmailIngestionBatchResult> PollAndProcessAsync(CancellationToken ct = default);
}

public interface IEmailParser
{
    // Parse raw MIME email bytes into structured EmailMessage.
    EmailMessage Parse(byte[] rawEmail);
}

public interface IEmailDuplicateDetector
{
    // Returns true if this email was already ingested.
    Task<bool> IsDuplicateAsync(EmailFingerprint fingerprint, CancellationToken ct = default);
    Task MarkProcessedAsync(EmailFingerprint fingerprint, CancellationToken ct = default);
}

public interface IEmailAttachmentStore
{
    // Store a single attachment, return its blob path.
    Task<string> StoreAsync(EmailAttachment attachment, string correlationId, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.EmailIngestion.Models;

public record EmailMessage
{
    public required string MessageId { get; init; }       // from email Message-ID header
    public required string Subject { get; init; }
    public required string FromAddress { get; init; }
    public required string FromName { get; init; }
    public required string? ToAddress { get; init; }
    public required string BodyText { get; init; }        // plain text part
    public required string? BodyHtml { get; init; }
    public required IReadOnlyList<EmailAttachment> Attachments { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required IDictionary<string, string> Headers { get; init; }
}

public record EmailAttachment
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Content { get; init; }
    public long SizeBytes => Content.Length;
}

public record EmailFingerprint(
    string FromAddress,
    string Subject,
    string? DateBucket,    // yyyy-MM-dd
    string? ContentHash    // MD5 of first attachment if present
);

// The domain event published to Service Bus after successful ingestion
public record EmailReceivedEvent
{
    public required string CorrelationId { get; init; }
    public required string MessageId { get; init; }       // email Message-ID
    public required string FromAddress { get; init; }
    public required string Subject { get; init; }
    public required string RawEmailBlobPath { get; init; }
    public required IReadOnlyList<string> AttachmentBlobPaths { get; init; }
    public required int AttachmentCount { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public string? DetectedIntentHint { get; init; }     // "NewSubmission", "Update", "Unknown"
}

public record EmailIngestionBatchResult(
    int TotalFetched,
    int NewlyProcessed,
    int DuplicatesSkipped,
    int Errors,
    DateTimeOffset ProcessedAt
);
```

---

## Configuration

```csharp
public class EmailIngestionOptions
{
    public EmailIngestionProvider Provider { get; set; } = EmailIngestionProvider.MicrosoftGraph;

    // Microsoft Graph
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }        // from Key Vault
    public string? MailboxAddress { get; set; }      // e.g. "submissions@company.com"
    public string? InboxFolderName { get; set; } = "Inbox";
    public string? ProcessedFolderName { get; set; } = "Processed";

    // Polling
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxEmailsPerBatch { get; set; } = 20;

    // Storage
    public string AttachmentContainerName { get; set; } = "email-attachments";
    public string AttachmentPathTemplate { get; set; } = "incoming/{year}/{month}/{day}/{correlationId}/{fileName}";

    // Service Bus
    public string EventTopic { get; set; } = "email-intake-events";

    // Duplicate detection window
    public TimeSpan DuplicateDetectionWindow { get; set; } = TimeSpan.FromDays(3);
}

public enum EmailIngestionProvider { MicrosoftGraph, Imap, Webhook }
```

---

## DI Registration

```csharp
builder.Services.AddKsEmailIngestion(options =>
{
    builder.Configuration.GetSection("KSquare:EmailIngestion").Bind(options);
    options.ClientSecret = builder.Configuration["Graph--ClientSecret"];
})
// Dependencies (these libraries must already be registered)
// .RequiresKsBlobStorage()
// .RequiresKsEventBus()
;
```

---

## Processing Flow (Detailed)

```
1. MicrosoftGraphEmailSource.FetchAsync()
   - Authenticate with ClientCredential (app-only)
   - GET /v1.0/users/{mailbox}/mailFolders/Inbox/messages?$top=20&$filter=isRead eq false
   - Use $select to limit fields (subject, from, receivedDateTime, hasAttachments)
   - For each message: GET /messages/{id}/$value (raw MIME bytes) 

2. IEmailParser.Parse(rawEmail)
   - Parse MIME using MimeKit library
   - Extract subject, from, body parts (text/plain preferred, text/html fallback)
   - Extract all attachments (non-inline, content-disposition = attachment)

3. IEmailDuplicateDetector.IsDuplicateAsync(fingerprint)
   - Hash: SHA256 of (fromAddress + normalizedSubject + dateBucket + firstAttachmentMD5)
   - Check processed_emails table or Redis for this hash within detection window
   - If duplicate: skip, log, increment DuplicatesSkipped

4. IEmailAttachmentStore.StoreAsync(attachment, correlationId)
   - Use IBlobStorageConnector (KSquare.BlobStorage) to upload each attachment
   - Apply path template with year/month/day/correlationId/{sanitized_fileName}
   - Store raw email body as {correlationId}/raw.eml

5. IEventPublisher.PublishAsync("email-intake-events", "email.received", EmailReceivedEvent)
   - Use KSquare.EventBus
   - Include all blob paths for downstream IDP processing

6. Mark email as read / move to Processed folder in Graph API

7. IEmailDuplicateDetector.MarkProcessedAsync(fingerprint)
```

---

## Intent Detection (Light Classification)

Before publishing the event, the connector does a lightweight subject/body scan:

```csharp
// Simple heuristic — not ML, not AI — just keyword matching
string? DetectIntentHint(EmailMessage email)
{
    var lower = email.Subject.ToLowerInvariant() + " " + email.BodyText[..Math.Min(500, email.BodyText.Length)];
    if (lower.Contains("new submission") || lower.Contains("new account") || lower.Contains("new business"))
        return "NewSubmission";
    if (lower.Contains("renewal") || lower.Contains("re-quote"))
        return "Renewal";
    if (lower.Contains("update") || lower.Contains("additional info") || lower.Contains("follow up"))
        return "Update";
    return "Unknown";
}
```

Full NLP classification is deferred to the Rules Engine (Component 14).

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Graph API throttled (429) | Exponential backoff; skip batch; retry next poll cycle |
| Attachment too large (>50 MB) | Store metadata-only record; publish `email.attachment_oversized` event |
| Blob storage unavailable | Log error; do NOT mark email as read; retry next cycle |
| Duplicate detected | Skip processing; mark as read; log with fingerprint |
| Parser exception (corrupt MIME) | Store raw bytes as-is; publish `email.parse_failed` event for human review |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.EmailIngestion at path: shared/KSquare.EmailIngestion/

This library reads emails from a monitored mailbox, parses them, stores attachments to Blob Storage,
and publishes EmailReceivedEvent to Service Bus. It is provider-agnostic for the email source.

Project structure:
  shared/KSquare.EmailIngestion/
  ├── KSquare.EmailIngestion.csproj
  ├── Contracts/
  │   ├── IEmailIngestionConnector.cs
  │   ├── IEmailParser.cs
  │   ├── IEmailDuplicateDetector.cs
  │   └── IEmailAttachmentStore.cs
  ├── Models/
  │   ├── EmailMessage.cs
  │   ├── EmailAttachment.cs
  │   ├── EmailFingerprint.cs
  │   ├── EmailReceivedEvent.cs
  │   └── EmailIngestionBatchResult.cs
  ├── Configuration/
  │   └── EmailIngestionOptions.cs
  ├── Providers/
  │   └── MicrosoftGraph/
  │       ├── GraphEmailSource.cs         ← calls MS Graph API to fetch raw emails
  │       └── GraphEmailMover.cs          ← marks read, moves to Processed folder
  ├── Internal/
  │   ├── MimeEmailParser.cs              ← uses MimeKit to parse MIME bytes
  │   ├── SqlDuplicateDetector.cs         ← stores fingerprint hashes in SQL
  │   ├── BlobAttachmentStore.cs          ← uses KSquare.BlobStorage
  │   └── IntentHintDetector.cs           ← keyword-based subject/body hint
  ├── HostedService/
  │   └── EmailIngestionHostedService.cs  ← IHostedService that polls on a timer
  └── Extensions/
      └── ServiceCollectionExtensions.cs

GraphEmailSource:
  - Authenticate using Microsoft.Graph SDK with ClientSecretCredential (app-only flow)
  - Call GET /v1.0/users/{mailbox}/mailFolders/Inbox/messages
    with query: $filter=isRead eq false&$top={MaxEmailsPerBatch}&$select=id,subject,from,receivedDateTime
  - For each message ID: GET /messages/{id}/$value to get raw MIME bytes
  - Return raw bytes + message ID for each

MimeEmailParser:
  - Use MimeKit.MimeMessage.Load(stream) to parse raw bytes
  - Extract TextBody, HtmlBody, Subject, From address/name, ReceivedAt
  - Walk message body parts: collect all parts with ContentDisposition = "attachment"
  - Return EmailMessage with all Attachments populated

SqlDuplicateDetector:
  - Table: email_fingerprints (hash NVARCHAR(64) PK, processed_at DATETIMEOFFSET, expires_at DATETIMEOFFSET)
  - IsDuplicateAsync: SELECT 1 WHERE hash = @hash AND expires_at > NOW()
  - MarkProcessedAsync: INSERT INTO email_fingerprints (upsert on conflict)
  - SHA256 fingerprint: SHA256(fromAddress.ToLower() + "||" + NormalizeSubject(subject) + "||" + dateBucket)

BlobAttachmentStore:
  - Inject IBlobStorageConnector (from KSquare.BlobStorage)
  - Apply path template from options
  - Upload each attachment with metadata: correlationId, fileName, contentType, source="email-ingestion"
  - Also upload raw email as {correlationId}/raw.eml with contentType="message/rfc822"

EmailIngestionHostedService:
  - IHostedService with a PeriodicTimer(options.PollingInterval)
  - On each tick: call IEmailIngestionConnector.PollAndProcessAsync
  - Log BatchResult including counts
  - On exception: log and continue (do not crash the host)

EmailIngestionConnector (main orchestrator):
  Implements IEmailIngestionConnector
  On PollAndProcessAsync:
    1. Fetch emails from GraphEmailSource
    2. For each: Parse → CheckDuplicate → StoreAttachments → PublishEvent → MoveToProcessed → MarkDuplicate
    3. Track counts (fetched, processed, duplicate, error)
    4. Return EmailIngestionBatchResult

ServiceCollectionExtensions:
  AddKsEmailIngestion(Action<EmailIngestionOptions>)
  - Registers all interfaces and implementations
  - Registers EmailIngestionHostedService as IHostedService
  - Throws if required dependencies (IBlobStorageConnector, IEventPublisher) not registered

NuGet packages:
  - Microsoft.Graph 5.x
  - Azure.Identity 1.x
  - MimeKit 4.x
  - Microsoft.EntityFrameworkCore.SqlServer 8.x (for duplicate detector)
  - Microsoft.Extensions.Hosting.Abstractions

Tests at shared/KSquare.EmailIngestion.Tests/:
  - MimeEmailParser correctly extracts subject, from, text body from a test .eml file (include fixture)
  - MimeEmailParser extracts attachments correctly
  - SqlDuplicateDetector returns false first call, true second call for same fingerprint
  - IntentHintDetector returns "NewSubmission" for expected keywords
  - EmailIngestionConnector (integration-style): use mock GraphEmailSource, real parser, InMemory blob
    → verify EmailReceivedEvent published with correct attachment blob paths
  Use xUnit + Moq + FluentAssertions.
```
