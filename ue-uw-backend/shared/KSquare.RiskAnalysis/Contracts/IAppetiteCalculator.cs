namespace KSquare.RiskAnalysis.Contracts;

using KSquare.RiskAnalysis.Models;

public interface IAppetiteCalculator
{
    Task<AppetiteFitResult> CalculateAsync(
        RiskIndicatorScores scores,
        RiskScoringContext context,
        CancellationToken ct = default
    );
}

