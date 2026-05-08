namespace KSquare.PolicyAdminAdapter.Database;

using KSquare.PolicyAdminAdapter.Models;

public sealed class BindJobRecord
{
    public required string BindJobId { get; set; }
    public required string QuoteId { get; set; }
    public required string SubmissionId { get; set; }
    public required PolicyAdminProvider Provider { get; set; }
    public string? ProviderTransactionId { get; set; }
    public required BindJobStatus Status { get; set; }
    public string? PolicyNumber { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

