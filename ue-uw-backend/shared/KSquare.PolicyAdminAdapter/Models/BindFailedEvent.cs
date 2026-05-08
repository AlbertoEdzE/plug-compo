namespace KSquare.PolicyAdminAdapter.Models;

public sealed record BindFailedEvent
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string BindJobId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required DateTimeOffset FailedAt { get; init; }
    public string? CorrelationId { get; init; }
}

