namespace KSquare.EmailSend.Models;

public record EmailMessage
{
    public required EmailAddress From { get; init; }
    public required IReadOnlyList<EmailAddress> To { get; init; }
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? TextBody { get; init; }
    public IReadOnlyList<EmailAttachmentRef> Attachments { get; init; } = [];
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public string? CorrelationId { get; init; }
    public string? ReplyToAddress { get; init; }
}
