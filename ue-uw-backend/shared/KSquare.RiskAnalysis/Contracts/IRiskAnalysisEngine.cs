namespace KSquare.RiskAnalysis.Contracts;

using KSquare.RiskAnalysis.Models;

public interface IRiskAnalysisEngine
{
    Task<RiskAnalysisResult> AnalyzeAsync(RiskAnalysisRequest request, CancellationToken ct = default);
}

