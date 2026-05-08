namespace KSquare.PolicyAdminAdapter.Models;

public sealed record BindRequest
{
    public required string QuoteId { get; init; }
    public required string SubmissionId { get; init; }
    public required string InstitutionLegalName { get; init; }
    public required string InstitutionDba { get; init; }
    public required string NaicsCode { get; init; }
    public required Address InstitutionAddress { get; init; }
    public required string ProducerLicenseNumber { get; init; }
    public required string ProducerCode { get; init; }
    public required string ProducerName { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required IReadOnlyList<BindCoverageLine> CoverageLines { get; init; }
    public required decimal TotalAnnualPremium { get; init; }
    public string? BrokerEmail { get; init; }
    public string? UnderwriterUserId { get; init; }
    public string? SpecialConditions { get; init; }
    public string? CorrelationId { get; init; }
}

