namespace KSquare.PolicyAdminAdapter.Models;

public sealed record BindJob
{
    public required string BindJobId { get; init; }
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required BindJobStatus Status { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? PolicyNumber { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryCount { get; init; } = 0;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
}

