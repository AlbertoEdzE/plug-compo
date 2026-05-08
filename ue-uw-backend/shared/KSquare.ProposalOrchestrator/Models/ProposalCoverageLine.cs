namespace KSquare.ProposalOrchestrator.Models;

public record ProposalCoverageLine
{
    public required string ProductName { get; init; }
    public required decimal Limit { get; init; }
    public required decimal Retention { get; init; }
    public required decimal AnnualPremium { get; init; }
    public decimal? AggregateLimit { get; init; }
    public string? CoverageConditions { get; init; }
}

