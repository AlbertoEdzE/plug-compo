namespace KSquare.ProposalOrchestrator.Models;

public record ProposalGenerationRequest
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string ProposalType { get; init; }
    public required string InstitutionName { get; init; }
    public required string BrokerName { get; init; }
    public required string BrokerEmail { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required IReadOnlyList<ProposalCoverageLine> CoverageLines { get; init; }
    public string? UnderwriterName { get; init; }
    public string? SpecialConditions { get; init; }
    public string? OutputFormat { get; init; } = "pdf";
    public string? CorrelationId { get; init; }
}

