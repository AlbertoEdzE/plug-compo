namespace KSquare.RiskAnalysis.Models;

public sealed record RiskScoringContext
{
    public required string SubmissionId { get; init; }
    public required string InstitutionType { get; init; }
    public required string NaicsCode { get; init; }
    public required int NumberOfLocations { get; init; }
    public required decimal TotalInsuredValue { get; init; }
    public required int NumberOfCoverageLines { get; init; }
    public required IReadOnlyList<string> CoverageLineNames { get; init; }
    public required IDictionary<string, string?> FormResponses { get; init; }
    public required LossRunSummary LossRunSummary { get; init; }
    public string? CorrelationId { get; init; }
}

