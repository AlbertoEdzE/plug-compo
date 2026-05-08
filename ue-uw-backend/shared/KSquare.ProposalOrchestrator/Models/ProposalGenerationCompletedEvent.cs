namespace KSquare.ProposalOrchestrator.Models;

public record ProposalGenerationCompletedEvent
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string JobId { get; init; }
    public required string BlobPath { get; init; }
    public required string SasUrl { get; init; }
    public required string ProposalType { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public string? CorrelationId { get; init; }
}

