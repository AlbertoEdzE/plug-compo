namespace KSquare.RiskAnalysis.Configuration;

public sealed class RiskAnalysisOptions
{
    public IDictionary<string, ScoringWeights> InstitutionTypeWeights { get; set; } = new Dictionary<string, ScoringWeights>
    {
        ["K-12 Public District"] = new ScoringWeights(0.30f, 0.30f, 0.20f, 0.20f),
        ["Higher Ed"] = new ScoringWeights(0.20f, 0.35f, 0.25f, 0.20f),
    };

    public decimal HighLossRatioThreshold { get; set; } = 0.65m;
    public decimal ExcessiveLossRatioThreshold { get; set; } = 0.85m;
    public int MinimumLossHistoryYears { get; set; } = 3;
    public IList<string> OutOfAppetiteNaicsCodes { get; set; } = Array.Empty<string>();
}

public sealed record ScoringWeights(float CampusSafety, float ClaimsSeverity, float PolicyComplexity, float LitigationExposure);

