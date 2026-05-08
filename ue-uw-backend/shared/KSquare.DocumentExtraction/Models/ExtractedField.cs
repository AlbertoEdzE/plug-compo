namespace KSquare.DocumentExtraction.Models;

public record ExtractedField
{
    public required string Name { get; init; }
    public required string? Value { get; init; }
    public required float Confidence { get; init; }
    public BoundingBox? BoundingBox { get; init; }
    public int? PageNumber { get; init; }
    public bool NeedsReview => Confidence < 0.75f;
}
