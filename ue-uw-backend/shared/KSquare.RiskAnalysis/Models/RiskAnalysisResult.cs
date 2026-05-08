namespace KSquare.RiskAnalysis.Models;

public sealed record RiskAnalysisResult
{
    public required string SubmissionId { get; init; }
    public required LossRunSummary LossRunSummary { get; init; }
    public required RiskIndicatorScores RiskIndicators { get; init; }
    public required AppetiteFitResult AppetiteFit { get; init; }
    public DateTimeOffset ComputedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}

