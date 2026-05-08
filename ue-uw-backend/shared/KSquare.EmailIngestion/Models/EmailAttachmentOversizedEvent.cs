namespace KSquare.EmailIngestion.Models;

public record EmailAttachmentOversizedEvent
{
    public required string CorrelationId { get; init; }
    public required string MessageId { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
}
