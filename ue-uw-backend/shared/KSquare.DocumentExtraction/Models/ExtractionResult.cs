namespace KSquare.DocumentExtraction.Models;

public record ExtractionResult
{
    public required string DocumentId { get; init; }
    public required string ProviderOperationId { get; init; }
    public required ExtractionStatus Status { get; init; }
    public required IReadOnlyList<ExtractedField> Fields { get; init; }
    public required IReadOnlyList<ExtractedTable> Tables { get; init; }
    public required IReadOnlyList<ExtractedPage> Pages { get; init; }
    public string? DetectedDocumentType { get; init; }
    public float OverallConfidence { get; init; }
    public DateTimeOffset ExtractedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? ModelUsed { get; init; }
    public string? CorrelationId { get; init; }
}
