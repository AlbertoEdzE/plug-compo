using KSquare.DocumentExtraction.Models;
using KSquare.RiskAnalysis.Configuration;
using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Models;

namespace KSquare.RiskAnalysis.Internal;

internal sealed class LossRunAnalyzer(RiskAnalysisOptions options) : ILossRunAnalyzer
{
    private readonly LossRunTableParser _parser = new();

    public LossRunSummary Analyze(IReadOnlyList<ExtractedTable> lossTables, string submissionId)
    {
        _ = submissionId;

        if (lossTables.Count == 0)
        {
            return new LossRunSummary
            {
                AnnualRecords = Array.Empty<AnnualLossRecord>(),
                FiveYearAverageLossRatio = 0m,
                TotalClaimsCount = 0,
                TotalIncurred = 0m,
                Trend = LossTrend.Insufficient,
                HasLitigatedClaims = false,
                LargestSingleLoss = null,
                DataYearsAvailable = 0
            };
        }

        var all = new List<AnnualLossRecord>();
        foreach (var table in lossTables)
        {
            all.AddRange(_parser.Parse(table));
        }

        var annual = all
            .GroupBy(r => r.Year)
            .Select(g => new AnnualLossRecord
            {
                Year = g.Key,
                ClaimsCount = g.Sum(x => x.ClaimsCount),
                TotalIncurred = g.Sum(x => x.TotalIncurred),
                LossRatio = g.Average(x => x.LossRatio),
                HasLitigatedClaims = g.Any(x => x.HasLitigatedClaims)
            })
            .OrderBy(r => r.Year)
            .ToList();

        var yearsAvailable = annual.Count;
        var totalClaims = annual.Sum(r => r.ClaimsCount);
        var totalIncurred = annual.Sum(r => r.TotalIncurred);
        var largest = annual.Count == 0 ? (decimal?)null : annual.Max(r => r.TotalIncurred);
        var litigated = annual.Any(r => r.HasLitigatedClaims);

        var avg5 = ComputeFiveYearAverageLossRatio(annual);
        var trend = ComputeTrend(annual, options.MinimumLossHistoryYears);

        return new LossRunSummary
        {
            AnnualRecords = annual,
            FiveYearAverageLossRatio = avg5,
            TotalClaimsCount = totalClaims,
            TotalIncurred = totalIncurred,
            Trend = trend,
            HasLitigatedClaims = litigated,
            LargestSingleLoss = largest,
            DataYearsAvailable = yearsAvailable
        };
    }

    private static decimal ComputeFiveYearAverageLossRatio(IReadOnlyList<AnnualLossRecord> annual)
    {
        if (annual.Count == 0)
        {
            return 0m;
        }

        var recent = annual.OrderByDescending(a => a.Year).Take(5).ToList();
        return recent.Average(a => a.LossRatio);
    }

    private static LossTrend ComputeTrend(IReadOnlyList<AnnualLossRecord> annual, int minYears)
    {
        if (annual.Count < minYears)
        {
            return LossTrend.Insufficient;
        }

        var ordered = annual.OrderBy(a => a.Year).ToList();
        if (ordered.Count < 5)
        {
            return LossTrend.Insufficient;
        }

        var last2 = ordered.TakeLast(2).Average(x => x.LossRatio);
        var prior3 = ordered.Skip(Math.Max(0, ordered.Count - 5)).Take(3).Average(x => x.LossRatio);

        if (prior3 <= 0m)
        {
            return LossTrend.Stable;
        }

        if (last2 < prior3 * 0.85m)
        {
            return LossTrend.Improving;
        }

        if (last2 > prior3 * 1.15m)
        {
            return LossTrend.Worsening;
        }

        return LossTrend.Stable;
    }
}

