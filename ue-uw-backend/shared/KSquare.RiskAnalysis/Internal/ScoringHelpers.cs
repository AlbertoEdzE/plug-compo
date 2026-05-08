using System.Globalization;
using System.Text.RegularExpressions;

namespace KSquare.RiskAnalysis.Internal;

internal static partial class ScoringHelpers
{
    public static string NormalizeHeader(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        lower = Regex.Replace(lower, @"[^\p{L}\p{Nd}]+", " ");
        return Regex.Replace(lower, @"\s+", " ").Trim();
    }

    public static int FindColumn(IReadOnlyList<string> headers, IReadOnlyList<string> aliases)
    {
        var normalizedAliases = aliases.Select(NormalizeHeader).Where(a => a.Length > 0).ToArray();
        var bestScore = 0;
        var bestIndex = -1;

        for (var i = 0; i < headers.Count; i++)
        {
            var h = NormalizeHeader(headers[i]);
            if (h.Length == 0)
            {
                continue;
            }

            foreach (var alias in normalizedAliases)
            {
                var score = SimilarityScore(h, alias);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
        }

        return bestScore >= 2 ? bestIndex : -1;
    }

    private static int SimilarityScore(string header, string alias)
    {
        if (header.Equals(alias, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (header.Contains(alias, StringComparison.OrdinalIgnoreCase) || alias.Contains(header, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        var headerTokens = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var aliasTokens = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var overlap = headerTokens.Intersect(aliasTokens, StringComparer.OrdinalIgnoreCase).Count();
        return overlap >= 2 ? 1 : 0;
    }

    public static string? SafeGet(IReadOnlyList<string?> row, int idx)
    {
        if (idx < 0 || idx >= row.Count)
        {
            return null;
        }

        return row[idx];
    }

    public static bool IsTotalsRow(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        return v.Contains("total", StringComparison.OrdinalIgnoreCase)
               || v.Contains("avg", StringComparison.OrdinalIgnoreCase)
               || v.Contains("average", StringComparison.OrdinalIgnoreCase)
               || v.Contains("5-year", StringComparison.OrdinalIgnoreCase)
               || v.Contains("5 year", StringComparison.OrdinalIgnoreCase)
               || v.Contains("5yr", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseYear(string value, out int year)
    {
        var match = Regex.Match(value, @"(?<!\d)(\d{4})(?!\d)");
        if (!match.Success)
        {
            year = 0;
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out year))
        {
            return false;
        }

        return year is >= 1900 and <= 2100;
    }

    public static int ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var cleaned = Regex.Replace(value, @"[^\d\-]+", "");
        return int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    public static decimal ParseCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var trimmed = value.Trim();
        var negative = trimmed.Contains('(') && trimmed.Contains(')');
        var cleaned = Regex.Replace(trimmed, @"[^\d\.\-]+", "");
        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            return 0m;
        }

        return negative ? -Math.Abs(parsed) : parsed;
    }

    public static decimal ParsePercentToFraction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var cleaned = value.Replace("%", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            return 0m;
        }

        if (parsed > 1m)
        {
            return parsed / 100m;
        }

        return parsed;
    }

    public static bool ParseYesNo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim().ToLowerInvariant();
        return v is "yes" or "y" or "true" or "1";
    }
}

