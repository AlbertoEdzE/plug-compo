using System.Text.Json.Serialization;

namespace KSquare.DocumentNarrative.Models;

public sealed record LossHistoryContext
{
    [JsonPropertyName("five_year_avg_loss_ratio")]
    public double FiveYearAvgLossRatio { get; init; }

    [JsonPropertyName("largest_single_loss")]
    public double LargestSingleLoss { get; init; }

    [JsonPropertyName("total_claims_count")]
    public int TotalClaimsCount { get; init; }

    [JsonPropertyName("loss_trend")]
    public required string LossTrend { get; init; }

    [JsonPropertyName("loss_run_years")]
    public IReadOnlyList<Dictionary<string, object?>> LossRunYears { get; init; } = Array.Empty<Dictionary<string, object?>>();
}
