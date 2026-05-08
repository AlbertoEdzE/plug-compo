using KSquare.ProposalOrchestrator.Models;

namespace KSquare.ProposalOrchestrator.Database;

public sealed class ProposalJobRecord
{
    public string JobId { get; set; } = string.Empty;
    public string QuoteId { get; set; } = string.Empty;
    public string SubmissionId { get; set; } = string.Empty;
    public string ProposalType { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderJobId { get; set; }
    public ProposalJobStatus Status { get; set; } = ProposalJobStatus.Pending;
    public int RetryCount { get; set; }
    public string? ArtifactBlobPath { get; set; }
    public string? ArtifactSasUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

