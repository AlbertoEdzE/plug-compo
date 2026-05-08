namespace KSquare.RiskAnalysis.Contracts;

using KSquare.RiskAnalysis.Models;

public interface IRiskScorer
{
    RiskIndicatorScores Score(RiskScoringContext context);
}

