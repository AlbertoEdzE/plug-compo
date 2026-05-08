namespace KSquare.RiskAnalysis.Contracts;

using KSquare.DocumentExtraction.Models;
using KSquare.RiskAnalysis.Models;

public interface ILossRunAnalyzer
{
    LossRunSummary Analyze(IReadOnlyList<ExtractedTable> lossTables, string submissionId);
}

