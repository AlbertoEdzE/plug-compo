namespace KSquare.ProposalOrchestrator.Models;

public record ProposalGenerationFailedEvent
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string JobId { get; init; }
    public required string ProposalType { get; init; }
    public required string ErrorMessage { get; init; }
    public required DateTimeOffset FailedAt { get; init; }
    public string? CorrelationId { get; init; }
}

