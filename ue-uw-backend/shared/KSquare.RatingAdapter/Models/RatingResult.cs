namespace KSquare.RatingAdapter.Models;

public record RatingResult
{
    public required string SubmissionId { get; init; }
    public required string QuoteId { get; init; }
    public required RatingStatus Status { get; init; }
    public required IReadOnlyList<CoverageLinePremium> PremiumLines { get; init; }
    public required decimal TotalAnnualPremium { get; init; }
    public string? RatingEngineReferenceId { get; init; }
    public string? RatingBasis { get; init; }
    public IReadOnlyList<RatingMessage> Messages { get; init; } = [];
    public DateTimeOffset RatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}

