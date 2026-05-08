namespace KSquare.DocumentClassification.Models;

public record ClassificationResult
{
    public required string DocumentType { get; init; }
    public required float Confidence { get; init; }
    public required ClassificationMethod Method { get; init; }
    public IReadOnlyList<ClassificationCandidate> AlternativeCandidates { get; init; } = [];
    public bool RequiresManualReview => Confidence < 0.70f || DocumentType == "Unknown";
    public string? CorrelationId { get; init; }
    public DateTimeOffset ClassifiedAt { get; init; } = DateTimeOffset.UtcNow;
}
