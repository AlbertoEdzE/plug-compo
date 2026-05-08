namespace KSquare.EmailIngestion.Models;

public record EmailReceivedEvent
{
    public required string CorrelationId { get; init; }
    public required string MessageId { get; init; }
    public required string FromAddress { get; init; }
    public required string Subject { get; init; }
    public required string RawEmailBlobPath { get; init; }
    public required IReadOnlyList<string> AttachmentBlobPaths { get; init; }
    public required int AttachmentCount { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public string? DetectedIntentHint { get; init; }
}
