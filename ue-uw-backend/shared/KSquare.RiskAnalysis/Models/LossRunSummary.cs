namespace KSquare.RiskAnalysis.Models;

public sealed record LossRunSummary
{
    public required IReadOnlyList<AnnualLossRecord> AnnualRecords { get; init; }
    public required decimal FiveYearAverageLossRatio { get; init; }
    public required int TotalClaimsCount { get; init; }
    public required decimal TotalIncurred { get; init; }
    public required LossTrend Trend { get; init; }
    public bool HasLitigatedClaims { get; init; }
    public decimal? LargestSingleLoss { get; init; }
    public int DataYearsAvailable { get; init; }
}

