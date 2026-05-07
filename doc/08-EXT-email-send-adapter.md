# Component 08 — Email Send Adapter

**Library**: `KSquare.EmailSend`  
**Layer**: Communication  
**Default Provider**: SendGrid  
**Alternate Providers**: SMTP (SmtpClient / MailKit), Azure Communication Services Email  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Outbound email is needed in multiple workbench flows:
- Broker notification when submission status changes
- Underwriter assignment notification
- Referral decision communication
- Quote / binder delivery with attached PDF
- System alert emails (failures, SLA breaches)

Without a shared library:
- Every service hard-codes SendGrid API calls with different retry logic
- Liquid/Scriban template rendering is duplicated
- Attachment handling (blobs fetched, streamed inline) is re-implemented per service
- Delivery tracking (sent, failed, bounced) is inconsistent

The complexity justifying a library:
- Provider abstraction (SendGrid vs SMTP vs ACS — each has very different SDK shape)
- Liquid template engine integration (templates stored as blob files, loaded on demand)
- Multi-part email: HTML body + plain text fallback + inline attachments
- Rate-limit and transient failure retry (SendGrid returns 429/503 transiently)
- Delivery event webhook ingestion (bounces, opens, clicks) as an optional side-channel

---

## Interface Contract

```csharp
namespace KSquare.EmailSend.Contracts;

public interface IEmailSender
{
    // Send a fully-formed email message.
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);

    // Render a named template with model data, then send.
    Task<EmailSendResult> SendTemplatedAsync<TModel>(
        string templateName,
        TModel model,
        EmailAddress to,
        string? subject = null,       // overrides template default if set
        IReadOnlyList<EmailAttachmentRef>? attachments = null,
        CancellationToken ct = default) where TModel : class;
}

public interface IEmailTemplateRenderer
{
    // Render a named template (Liquid syntax) against a model, returning HTML + text.
    Task<RenderedEmail> RenderAsync<TModel>(string templateName, TModel model, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.EmailSend.Models;

public record EmailAddress(string Address, string? DisplayName = null);

public record EmailMessage
{
    public required EmailAddress From { get; init; }
    public required IReadOnlyList<EmailAddress> To { get; init; }
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? TextBody { get; init; }               // auto-generated from HTML if null
    public IReadOnlyList<EmailAttachmentRef> Attachments { get; init; } = [];
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public string? CorrelationId { get; init; }
    public string? ReplyToAddress { get; init; }
}

// Reference to an attachment — either raw bytes or a blob path fetched at send time
public record EmailAttachmentRef
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    // Exactly one of the following must be set:
    public byte[]? Content { get; init; }
    public string? BlobPath { get; init; }    // fetched via IBlobStorageConnector
}

public record RenderedEmail(string Subject, string HtmlBody, string TextBody);

public record EmailSendResult(
    bool Success,
    string? ProviderMessageId,      // SendGrid message ID for tracking
    string? Error,
    DateTimeOffset SentAt
);
```

---

## Configuration

```csharp
public class EmailSendOptions
{
    public EmailSendProvider Provider { get; set; } = EmailSendProvider.SendGrid;

    // SendGrid
    public string? SendGridApiKey { get; set; }       // from Key Vault

    // SMTP fallback
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;

    // Default sender
    public string DefaultFromAddress { get; set; } = "noreply@company.com";
    public string DefaultFromName { get; set; } = "UW Workbench";

    // Template store
    public EmailTemplateSource TemplateSource { get; set; } = EmailTemplateSource.EmbeddedResource;
    public string? TemplateBlobContainerName { get; set; } = "email-templates";

    // Retry
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}

public enum EmailSendProvider { SendGrid, Smtp, AzureCommunicationServices, InMemory }
public enum EmailTemplateSource { EmbeddedResource, BlobStorage, FileSystem }
```

---

## DI Registration

```csharp
builder.Services.AddKsEmailSend(options =>
{
    builder.Configuration.GetSection("KSquare:EmailSend").Bind(options);
    options.SendGridApiKey = builder.Configuration["SendGrid--ApiKey"];
    options.TemplateSource = EmailTemplateSource.BlobStorage;
    options.TemplateBlobContainerName = "email-templates";
});
// Requires KSquare.BlobStorage to be registered if BlobStorage template source is used.
```

---

## Built-In Templates

| Template Name | Used By | Model Type |
|---|---|---|
| `submission-status-changed` | submission-api | `{ SubmissionNumber, BrokerName, OldStatus, NewStatus, PortalUrl }` |
| `assignment-notification` | underwriting-api | `{ UnderwriterName, SubmissionNumber, BrokerName, DueDate }` |
| `referral-decision` | underwriting-api | `{ BrokerName, SubmissionNumber, Decision, Reason, NextSteps }` |
| `quote-ready` | quote-api | `{ BrokerName, SubmissionNumber, QuoteNumber, ExpiryDate, PortalUrl }` |
| `document-request` | underwriting-api | `{ BrokerName, SubmissionNumber, DocumentList[] }` |

Templates are Liquid syntax (`.liquid.html` + `.liquid.txt` pairs) stored as embedded resources or in Blob Storage.

---

## Usage Examples

```csharp
// Simple send
await emailSender.SendAsync(new EmailMessage
{
    From = new EmailAddress("uw@company.com", "UW Team"),
    To = [new EmailAddress(broker.Email, broker.DisplayName)],
    Subject = "Your submission has been assigned",
    HtmlBody = "<p>Your submission <b>SUB-0042</b> has been assigned to an underwriter.</p>",
    CorrelationId = correlationId
});

// Template send
await emailSender.SendTemplatedAsync(
    templateName: "submission-status-changed",
    model: new
    {
        SubmissionNumber = "SUB-0042",
        BrokerName = "Jane Smith",
        OldStatus = "Draft",
        NewStatus = "Submitted",
        PortalUrl = "https://uw.company.com/submissions/0042"
    },
    to: new EmailAddress(broker.Email, broker.DisplayName)
);

// With PDF attachment (fetched from Blob)
await emailSender.SendTemplatedAsync(
    templateName: "quote-ready",
    model: quoteModel,
    to: brokerAddress,
    attachments: [new EmailAttachmentRef
    {
        FileName = "Quote-0042.pdf",
        ContentType = "application/pdf",
        BlobPath = "quotes/2024/01/15/quote-0042.pdf"
    }]
);
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| SendGrid 429 (rate limit) | Polly exponential backoff; up to MaxRetryAttempts |
| SendGrid 4xx (permanent) | Return `EmailSendResult { Success = false, Error = ... }`; log; do not retry |
| Blob attachment fetch fails | Throw `EmailSendException`; caller decides to send without attachment or abort |
| Template not found | Throw `EmailTemplateNotFoundException` with template name in message |
| Invalid Liquid template | Throw `EmailTemplateRenderException` with line number from Liquid parser |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.EmailSend at path: shared/KSquare.EmailSend/

This library sends transactional emails via SendGrid (primary) or SMTP (fallback).
It supports Liquid templating, blob-fetched attachments, and retry with Polly.

Project structure:
  shared/KSquare.EmailSend/
  ├── KSquare.EmailSend.csproj
  ├── Contracts/
  │   ├── IEmailSender.cs
  │   └── IEmailTemplateRenderer.cs
  ├── Models/
  │   ├── EmailAddress.cs
  │   ├── EmailMessage.cs
  │   ├── EmailAttachmentRef.cs
  │   ├── RenderedEmail.cs
  │   └── EmailSendResult.cs
  ├── Configuration/
  │   └── EmailSendOptions.cs
  ├── Providers/
  │   ├── SendGridEmailSender.cs
  │   ├── SmtpEmailSender.cs
  │   └── InMemoryEmailSender.cs     ← stores sent messages in List<EmailMessage> for tests
  ├── Templates/
  │   ├── LiquidTemplateRenderer.cs
  │   ├── TemplateLoader/
  │   │   ├── ITemplateLoader.cs
  │   │   ├── EmbeddedResourceTemplateLoader.cs
  │   │   └── BlobTemplateLoader.cs
  │   └── Resources/                 ← embedded .liquid.html and .liquid.txt files
  │       ├── submission-status-changed.liquid.html
  │       ├── submission-status-changed.liquid.txt
  │       └── ... (other templates)
  ├── Internal/
  │   └── HtmlToTextConverter.cs     ← strip HTML tags for auto-generating text body
  └── Extensions/
      └── ServiceCollectionExtensions.cs

SendGridEmailSender:
  - Use SendGrid.SendGridClient with SendGridApiKey
  - Build SendGridMessage from EmailMessage (From, To, Subject, HtmlContent, PlainTextContent)
  - For each EmailAttachmentRef:
    - If Content is set: attach bytes directly
    - If BlobPath is set: call IBlobStorageConnector.DownloadAsync, attach stream
  - Send via client.SendEmailAsync()
  - Wrap in Polly retry: RetryAsync(MaxRetryAttempts) with exponential backoff on HttpRequestException + SendGrid 429/503
  - Return EmailSendResult with StatusCode-based success check and Message-Id header

SmtpEmailSender:
  - Use MailKit.Net.Smtp.SmtpClient
  - Build MimeKit.MimeMessage from EmailMessage
  - Connect with STARTTLS if SmtpUseSsl = true
  - Authenticate with SmtpUsername/SmtpPassword
  - Send and disconnect

LiquidTemplateRenderer:
  - Use Fluid library (Fluid.Core) for Liquid template parsing
  - ITemplateLoader determines where to load .liquid.html and .liquid.txt from
  - Load both HTML and text variants; render each against model using FluidParser
  - Cache parsed FluidTemplate instances in ConcurrentDictionary<string, FluidTemplate>
  - Return RenderedEmail(Subject extracted from first <title> or <h1>, HtmlBody, TextBody)

EmbeddedResourceTemplateLoader:
  - Use Assembly.GetManifestResourceStream("KSquare.EmailSend.Templates.Resources.{name}.liquid.html")

BlobTemplateLoader:
  - Use IBlobStorageConnector.DownloadAsync(containerName, "{name}.liquid.html")
  - Cache blob content in-memory for 5 minutes (avoid re-downloading per send)

InMemoryEmailSender:
  - Store all sent EmailMessage objects in a public List<EmailMessage> SentMessages
  - Always returns EmailSendResult { Success = true }
  - Useful in tests to assert emails were sent with correct content

ServiceCollectionExtensions:
  AddKsEmailSend(Action<EmailSendOptions>):
  - Registers IEmailSender based on Provider option
  - Registers IEmailTemplateRenderer (LiquidTemplateRenderer)
  - Registers appropriate ITemplateLoader
  - If BlobStorage template source: requires IBlobStorageConnector to be registered

NuGet packages:
  - SendGrid 9.x
  - MailKit 4.x (SMTP provider)
  - Fluid.Core 2.x (Liquid templating)
  - Polly 8.x
  - Microsoft.Extensions.Http

Templates to include as embedded resources (minimal working versions):
  submission-status-changed.liquid.html:
    <h2>Submission Status Update</h2>
    <p>Dear {{ BrokerName }},</p>
    <p>Submission <strong>{{ SubmissionNumber }}</strong> status changed from {{ OldStatus }} to <strong>{{ NewStatus }}</strong>.</p>
    <p><a href="{{ PortalUrl }}">View in Portal</a></p>

  submission-status-changed.liquid.txt:
    Submission Status Update
    Dear {{ BrokerName }},
    Submission {{ SubmissionNumber }} status changed from {{ OldStatus }} to {{ NewStatus }}.
    Portal: {{ PortalUrl }}

Tests at shared/KSquare.EmailSend.Tests/ (use InMemory provider):
  - SendAsync sends email and stores in InMemoryEmailSender.SentMessages
  - SendTemplatedAsync renders submission-status-changed template with correct subject
  - Template renders BrokerName and SubmissionNumber correctly
  - Attachment with Content bytes is added to sent message
  - Missing template name throws EmailTemplateNotFoundException
  Use xUnit + FluentAssertions.
```
