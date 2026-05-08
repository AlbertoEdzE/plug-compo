namespace KSquare.EmailIngestion.Models;

public record EmailMessage
{
    public required string MessageId { get; init; }
    public required string Subject { get; init; }
    public required string FromAddress { get; init; }
    public required string FromName { get; init; }
    public required string? ToAddress { get; init; }
    public required string BodyText { get; init; }
    public required string? BodyHtml { get; init; }
    public required IReadOnlyList<EmailAttachment> Attachments { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required IDictionary<string, string> Headers { get; init; }
}
