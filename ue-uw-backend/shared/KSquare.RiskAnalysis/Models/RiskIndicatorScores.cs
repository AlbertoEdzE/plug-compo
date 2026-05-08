namespace KSquare.RiskAnalysis.Models;

public sealed record RiskIndicatorScores
{
    public required ScoreWithFactors CampusSafetyRating { get; init; }
    public required ScoreWithFactors ClaimsSeverity { get; init; }
    public required ScoreWithFactors PolicyComplexity { get; init; }
    public required ScoreWithFactors LitigationExposure { get; init; }

    public decimal CompositeRiskScore => (
        CampusSafetyRating.Score * 0.30m +
        (100 - ClaimsSeverity.Score) * 0.30m +
        (100 - PolicyComplexity.Score) * 0.20m +
        (100 - LitigationExposure.Score) * 0.20m
    );
}

