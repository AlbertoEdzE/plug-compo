namespace KSquare.EmailIngestion.Models;

public record EmailParseFailedEvent
{
    public required string CorrelationId { get; init; }
    public required string SourceMessageId { get; init; }
    public required string RawEmailBlobPath { get; init; }
    public required string Error { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
}
