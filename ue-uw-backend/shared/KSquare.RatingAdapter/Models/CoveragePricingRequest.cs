namespace KSquare.RatingAdapter.Models;

public record CoveragePricingRequest
{
    public required string SubmissionId { get; init; }
    public required string QuoteId { get; init; }
    public required string InstitutionType { get; init; }
    public required string NaicsCode { get; init; }
    public required string State { get; init; }
    public required int NumberOfLocations { get; init; }
    public required int TotalEnrollment { get; init; }
    public required int FteEmployees { get; init; }
    public required decimal TotalInsuredValue { get; init; }
    public required decimal OperatingExpenses { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required IReadOnlyList<CoverageLineRequest> CoverageLines { get; init; }
    public required LossHistorySummary LossHistory { get; init; }
    public string? CorrelationId { get; init; }
}

