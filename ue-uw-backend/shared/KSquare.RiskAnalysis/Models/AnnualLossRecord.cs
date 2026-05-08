namespace KSquare.RiskAnalysis.Models;

public sealed record AnnualLossRecord
{
    public required int Year { get; init; }
    public required int ClaimsCount { get; init; }
    public required decimal TotalIncurred { get; init; }
    public required decimal LossRatio { get; init; }
    public bool HasLitigatedClaims { get; init; }
}

