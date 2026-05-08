namespace KSquare.ProposalOrchestrator.Models;

public record ProposalGenerationJob
{
    public required string JobId { get; init; }
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required ProposalJobStatus Status { get; init; }
    public string? ProviderJobId { get; init; }
    public string? ArtifactBlobPath { get; init; }
    public string? ArtifactSasUrl { get; init; }
    public int RetryCount { get; init; } = 0;
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
}

