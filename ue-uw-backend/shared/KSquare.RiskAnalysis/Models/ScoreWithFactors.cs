namespace KSquare.RiskAnalysis.Models;

public sealed record ScoreWithFactors
{
    public required int Score { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<ScoringFactor> Factors { get; init; }
}

