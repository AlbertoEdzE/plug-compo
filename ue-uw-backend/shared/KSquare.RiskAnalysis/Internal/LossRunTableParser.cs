using KSquare.DocumentExtraction.Models;
using KSquare.RiskAnalysis.Models;

namespace KSquare.RiskAnalysis.Internal;

internal sealed class LossRunTableParser
{
    private static readonly string[] YearColumns = ["year", "policy year", "year of loss"];
    private static readonly string[] ClaimsColumns = ["claims", "claim count", "number of claims", "occurrences"];
    private static readonly string[] IncurredColumns = ["incurred", "total incurred", "losses", "paid reserved", "paid + reserved"];
    private static readonly string[] RatioColumns = ["ratio", "loss ratio", "l r", "l/r"];
    private static readonly string[] LitigatedColumns = ["litigated", "litigation", "suit", "lawsuit"];

    public IReadOnlyList<AnnualLossRecord> Parse(ExtractedTable table)
    {
        var yearIdx = ScoringHelpers.FindColumn(table.Headers, YearColumns);
        var claimsIdx = ScoringHelpers.FindColumn(table.Headers, ClaimsColumns);
        var incurredIdx = ScoringHelpers.FindColumn(table.Headers, IncurredColumns);
        var ratioIdx = ScoringHelpers.FindColumn(table.Headers, RatioColumns);
        var litigatedIdx = ScoringHelpers.FindColumn(table.Headers, LitigatedColumns);

        var records = new List<AnnualLossRecord>();
        foreach (var row in table.Rows)
        {
            var yearCell = yearIdx >= 0 ? ScoringHelpers.SafeGet(row, yearIdx) : null;
            if (string.IsNullOrWhiteSpace(yearCell) || ScoringHelpers.IsTotalsRow(yearCell))
            {
                continue;
            }

            if (!ScoringHelpers.TryParseYear(yearCell, out var year))
            {
                continue;
            }

            var claims = ScoringHelpers.ParseInt(ScoringHelpers.SafeGet(row, claimsIdx));
            var incurred = ScoringHelpers.ParseCurrency(ScoringHelpers.SafeGet(row, incurredIdx));
            var ratio = ScoringHelpers.ParsePercentToFraction(ScoringHelpers.SafeGet(row, ratioIdx));
            var litigated = ScoringHelpers.ParseYesNo(ScoringHelpers.SafeGet(row, litigatedIdx));

            records.Add(new AnnualLossRecord
            {
                Year = year,
                ClaimsCount = claims,
                TotalIncurred = incurred,
                LossRatio = ratio,
                HasLitigatedClaims = litigated
            });
        }

        return records
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
    }
}

