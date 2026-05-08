using System.Text.Json.Serialization;

namespace KSquare.DocumentNarrative.Models;

public sealed record SubmissionContext
{
    [JsonPropertyName("submission_id")]
    public required string SubmissionId { get; init; }

    [JsonPropertyName("institution_name")]
    public required string InstitutionName { get; init; }

    [JsonPropertyName("institution_type")]
    public required string InstitutionType { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("naics_code")]
    public required string NaicsCode { get; init; }

    [JsonPropertyName("total_insured_value")]
    public double TotalInsuredValue { get; init; }

    [JsonPropertyName("enrollment")]
    public int Enrollment { get; init; }

    [JsonPropertyName("fte_employees")]
    public int FteEmployees { get; init; }

    [JsonPropertyName("effective_date")]
    public required string EffectiveDate { get; init; }

    [JsonPropertyName("expiration_date")]
    public required string ExpirationDate { get; init; }

    [JsonPropertyName("coverage_lines")]
    public IReadOnlyList<Dictionary<string, object?>> CoverageLines { get; init; } = Array.Empty<Dictionary<string, object?>>();

    [JsonPropertyName("risk_indicators")]
    public Dictionary<string, object?> RiskIndicators { get; init; } = new();

    [JsonPropertyName("appetite_fit_score")]
    public double AppetiteFitScore { get; init; }

    [JsonPropertyName("appetite_classification")]
    public required string AppetiteClassification { get; init; }
}
