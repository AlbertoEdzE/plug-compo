using KSquare.RiskAnalysis.Configuration;
using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Models;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Models;

namespace KSquare.RiskAnalysis.Internal;

internal sealed class AppetiteCalculatorImpl(IRulesEngine rules, RiskAnalysisOptions options) : IAppetiteCalculator
{
    public async Task<AppetiteFitResult> CalculateAsync(RiskIndicatorScores scores, RiskScoringContext context, CancellationToken ct = default)
    {
        var appetiteContext = new AppetiteScoringContext
        {
            NaicsCode = context.NaicsCode,
            OutOfAppetiteNaicsCodes = options.OutOfAppetiteNaicsCodes.ToArray(),
            FiveYearAverageLossRatio = context.LossRunSummary.FiveYearAverageLossRatio,
            TotalInsuredValue = context.TotalInsuredValue,
            IntercollegiateFootballRevenuePercent = ParseIntercollegiateFootballRevenuePercent(context.FormResponses)
        };

        RuleEvaluationResult? evaluated = null;
        try
        {
            evaluated = await rules.EvaluateAsync("appetite-scoring", appetiteContext, ct);
        }
        catch
        {
            evaluated = null;
        }

        var firedRuleNames = evaluated?.Results.Where(r => r.Fired).Select(r => r.RuleName).ToList()
                            ?? new List<string>();
        var firedReasons = evaluated?.Results.Where(r => r.Fired).Select(r => r.Reason).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!).ToList()
                           ?? new List<string>();
        var firedActions = evaluated?.FiredActions ?? Array.Empty<string>();

        if (firedActions.Any(a => a.Equals("Decline", StringComparison.OrdinalIgnoreCase)))
        {
            return new AppetiteFitResult
            {
                Score = 0.0f,
                Classification = "Out of Appetite",
                FiredRules = firedRuleNames,
                RisksIdentified = firedReasons,
                RequiresReferral = true,
                ReferralReason = firedReasons.FirstOrDefault()
            };
        }

        var baseScore = (float)Math.Clamp(scores.CompositeRiskScore / 100m, 0m, 1m);

        var modifier = ExtractModifierFromActions(firedActions);
        if (modifier is null)
        {
            modifier = ComputeLossRatioModifier(context.LossRunSummary.FiveYearAverageLossRatio, options);
        }

        var final = (float)Math.Clamp(baseScore * modifier.Value, 0.0f, 1.0f);
        var classification = final >= 0.80f ? "In Appetite" : final >= 0.60f ? "Borderline" : "Out of Appetite";

        var requiresReferral = classification != "In Appetite";
        var referralReason = requiresReferral ? (firedReasons.FirstOrDefault() ?? "Risk score below appetite threshold") : null;

        return new AppetiteFitResult
        {
            Score = final,
            Classification = classification,
            FiredRules = firedRuleNames,
            RisksIdentified = firedReasons,
            RequiresReferral = requiresReferral,
            ReferralReason = referralReason
        };
    }

    private static float? ExtractModifierFromActions(IReadOnlyList<string> actions)
    {
        float modifier = 1.0f;
        var any = false;

        foreach (var action in actions)
        {
            if (!action.StartsWith("Modifier:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var part = action.Substring("Modifier:".Length);
            if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                modifier *= parsed;
                any = true;
            }
        }

        return any ? modifier : null;
    }

    private static float ComputeLossRatioModifier(decimal fiveYearAvg, RiskAnalysisOptions opts)
    {
        if (fiveYearAvg > opts.ExcessiveLossRatioThreshold)
        {
            return 0.70f;
        }

        if (fiveYearAvg > opts.HighLossRatioThreshold)
        {
            return 0.85f;
        }

        if (fiveYearAvg < 0.30m)
        {
            return 1.05f;
        }

        return 1.0f;
    }

    private static float ParseIntercollegiateFootballRevenuePercent(IDictionary<string, string?> responses)
    {
        var raw = TryGet(responses, "IntercollegiateFootballRevenuePercent")
                  ?? TryGet(responses, "IntercollegiateFootballRevenuePct")
                  ?? TryGet(responses, "IntercollFBRevenuePercent");

        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0.0f;
        }

        var fraction = (float)ScoringHelpers.ParsePercentToFraction(raw);
        return Math.Clamp(fraction, 0.0f, 1.0f);
    }

    private static string? TryGet(IDictionary<string, string?> responses, string key)
    {
        return responses.TryGetValue(key, out var v) ? v : null;
    }

    private sealed class AppetiteScoringContext
    {
        public string NaicsCode { get; init; } = "";
        public IReadOnlyList<string> OutOfAppetiteNaicsCodes { get; init; } = Array.Empty<string>();
        public decimal FiveYearAverageLossRatio { get; init; }
        public decimal TotalInsuredValue { get; init; }
        public float IntercollegiateFootballRevenuePercent { get; init; }
    }
}

