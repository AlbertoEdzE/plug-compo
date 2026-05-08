namespace KSquare.RatingAdapter.Models;

public record LossHistorySummary
{
    public required decimal FiveYearAverageLossRatio { get; init; }
    public required decimal LargestSingleLoss { get; init; }
    public required int TotalClaimsCount { get; init; }
    public required int DataYearsAvailable { get; init; }
}

