using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Models;

namespace KSquare.RiskAnalysis.Internal;

internal sealed class RiskAnalysisEngine(
    ILossRunAnalyzer lossRunAnalyzer,
    IRiskScorer scorer,
    IAppetiteCalculator appetite
) : IRiskAnalysisEngine
{
    public async Task<RiskAnalysisResult> AnalyzeAsync(RiskAnalysisRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var lossSummary = lossRunAnalyzer.Analyze(request.LossRunTables, request.SubmissionId);
        var scoringContext = new RiskScoringContext
        {
            SubmissionId = request.SubmissionId,
            InstitutionType = request.InstitutionType,
            NaicsCode = request.NaicsCode,
            NumberOfLocations = request.NumberOfLocations,
            TotalInsuredValue = request.TotalInsuredValue,
            NumberOfCoverageLines = request.NumberOfCoverageLines,
            CoverageLineNames = request.CoverageLineNames,
            FormResponses = request.FormResponses,
            LossRunSummary = lossSummary,
            CorrelationId = request.CorrelationId
        };

        var scores = scorer.Score(scoringContext);

        AppetiteFitResult appetiteFit;
        try
        {
            appetiteFit = await appetite.CalculateAsync(scores, scoringContext, ct);
        }
        catch
        {
            var baseScore = (float)Math.Clamp(scores.CompositeRiskScore / 100m, 0m, 1m);
            var classification = baseScore >= 0.80f ? "In Appetite" : baseScore >= 0.60f ? "Borderline" : "Out of Appetite";
            appetiteFit = new AppetiteFitResult
            {
                Score = baseScore,
                Classification = classification,
                FiredRules = Array.Empty<string>(),
                RisksIdentified = Array.Empty<string>(),
                RequiresReferral = classification != "In Appetite",
                ReferralReason = classification == "In Appetite" ? null : "Rules engine unavailable"
            };
        }

        return new RiskAnalysisResult
        {
            SubmissionId = request.SubmissionId,
            LossRunSummary = lossSummary,
            RiskIndicators = scores,
            AppetiteFit = appetiteFit,
            CorrelationId = request.CorrelationId
        };
    }
}

