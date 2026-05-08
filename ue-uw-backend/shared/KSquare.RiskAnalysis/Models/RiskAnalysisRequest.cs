namespace KSquare.RiskAnalysis.Models;

using KSquare.DocumentExtraction.Models;

public sealed record RiskAnalysisRequest
{
    public required string SubmissionId { get; init; }
    public required string InstitutionType { get; init; }
    public required string NaicsCode { get; init; }
    public required int NumberOfLocations { get; init; }
    public required decimal TotalInsuredValue { get; init; }
    public required int NumberOfCoverageLines { get; init; }
    public required IReadOnlyList<string> CoverageLineNames { get; init; }
    public required IDictionary<string, string?> FormResponses { get; init; }
    public IReadOnlyList<ExtractedTable> LossRunTables { get; init; } = Array.Empty<ExtractedTable>();
    public string? CorrelationId { get; init; }
}

