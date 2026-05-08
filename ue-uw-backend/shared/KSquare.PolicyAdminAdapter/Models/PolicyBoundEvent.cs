namespace KSquare.PolicyAdminAdapter.Models;

public sealed record PolicyBoundEvent
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string BindJobId { get; init; }
    public required string PolicyNumber { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required decimal TotalAnnualPremium { get; init; }
    public required DateTimeOffset BoundAt { get; init; }
    public string? CorrelationId { get; init; }
}

