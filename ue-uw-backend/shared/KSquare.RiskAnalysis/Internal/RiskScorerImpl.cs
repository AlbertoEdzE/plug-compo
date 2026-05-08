using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Models;

namespace KSquare.RiskAnalysis.Internal;

internal sealed class RiskScorerImpl : IRiskScorer
{
    public RiskIndicatorScores Score(RiskScoringContext context)
    {
        var campus = ScoreCampusSafety(context.FormResponses);
        var severity = ScoreClaimsSeverity(context.TotalInsuredValue, context.LossRunSummary);
        var complexity = ScorePolicyComplexity(context.NumberOfLocations, context.NumberOfCoverageLines, context.CoverageLineNames, context.FormResponses);
        var litigation = ScoreLitigationExposure(context.LossRunSummary, context.CoverageLineNames, context.FormResponses);

        ValidateScore(campus.Score, nameof(RiskIndicatorScores.CampusSafetyRating));
        ValidateScore(severity.Score, nameof(RiskIndicatorScores.ClaimsSeverity));
        ValidateScore(complexity.Score, nameof(RiskIndicatorScores.PolicyComplexity));
        ValidateScore(litigation.Score, nameof(RiskIndicatorScores.LitigationExposure));

        return new RiskIndicatorScores
        {
            CampusSafetyRating = campus,
            ClaimsSeverity = severity,
            PolicyComplexity = complexity,
            LitigationExposure = litigation
        };
    }

    private static ScoreWithFactors ScoreCampusSafety(IDictionary<string, string?> responses)
    {
        var score = 0;
        var factors = new List<ScoringFactor>();

        score += YesNoFactor(responses, "SecurityPersonnelOnSite", 20, factors);
        score += YesNoFactor(responses, "SurveillanceCamerasInstalled", 15, factors);
        score += YesNoFactor(responses, "EmergencyResponsePlanInPlace", 20, factors);
        score += YesNoFactor(responses, "SafetyTrainingConductedAnnually", 15, factors);
        score += YesNoFactor(responses, "IncidentReportingSystem", 10, factors);
        score += YesNoFactor(responses, "CrisisManagementTeam", 10, factors);
        score += YesNoFactor(responses, "SchoolResourceOfficer", 10, factors);

        var incidents = TryGetInt(responses, "RecentSecurityIncidents");
        if (incidents > 3)
        {
            score -= 30;
            factors.Add(new ScoringFactor("RecentSecurityIncidents", incidents.ToString(), -30m));
        }
        else if (incidents >= 1)
        {
            score -= 15;
            factors.Add(new ScoringFactor("RecentSecurityIncidents", incidents.ToString(), -15m));
        }
        else
        {
            factors.Add(new ScoringFactor("RecentSecurityIncidents", incidents.ToString(), 0m));
        }

        var trespass = TryGetInt(responses, "TrespassingIncidents");
        if (trespass > 0)
        {
            score -= 10;
            factors.Add(new ScoringFactor("TrespassingIncidents", trespass.ToString(), -10m));
        }
        else
        {
            factors.Add(new ScoringFactor("TrespassingIncidents", trespass.ToString(), 0m));
        }

        score = Clamp0To100(score);
        return new ScoreWithFactors
        {
            Score = score,
            Label = LabelPositiveHigh(score),
            Factors = factors
        };
    }

    private static ScoreWithFactors ScoreClaimsSeverity(decimal totalInsuredValue, LossRunSummary loss)
    {
        var ratio = 0m;
        if (totalInsuredValue > 0m && loss.LargestSingleLoss is not null && loss.LargestSingleLoss.Value > 0m)
        {
            ratio = loss.LargestSingleLoss.Value / totalInsuredValue;
        }

        var severity = SeverityFromRatio(ratio);
        var factors = new List<ScoringFactor>
        {
            new("LargestSingleLossToTiv", ratio.ToString("0.####"), severity)
        };

        var score = Clamp0To100((int)Math.Round(severity, MidpointRounding.AwayFromZero));
        return new ScoreWithFactors
        {
            Score = score,
            Label = LabelRiskHigh(score),
            Factors = factors
        };
    }

    private static decimal SeverityFromRatio(decimal ratio)
    {
        if (ratio <= 0.000m)
        {
            return 0m;
        }

        if (ratio <= 0.001m)
        {
            return 10m;
        }

        if (ratio <= 0.01m)
        {
            var t = (ratio - 0.001m) / (0.009m);
            return 10m + (70m * t);
        }

        var capped = Math.Min(0.05m, ratio);
        var t2 = (capped - 0.01m) / 0.04m;
        return 80m + (20m * t2);
    }

    private static ScoreWithFactors ScorePolicyComplexity(
        int locations,
        int coverageLines,
        IReadOnlyList<string> coverageLineNames,
        IDictionary<string, string?> responses
    )
    {
        var score = 0;
        var factors = new List<ScoringFactor>();

        if (locations > 10)
        {
            score += 20;
            factors.Add(new ScoringFactor("NumberOfLocations", locations.ToString(), 20m));
        }
        else if (locations >= 5)
        {
            score += 10;
            factors.Add(new ScoringFactor("NumberOfLocations", locations.ToString(), 10m));
        }
        else
        {
            factors.Add(new ScoringFactor("NumberOfLocations", locations.ToString(), 0m));
        }

        if (coverageLines > 4)
        {
            score += 15;
            factors.Add(new ScoringFactor("NumberOfCoverageLines", coverageLines.ToString(), 15m));
        }
        else
        {
            factors.Add(new ScoringFactor("NumberOfCoverageLines", coverageLines.ToString(), 0m));
        }

        foreach (var flag in new[] { "Athletic", "SexualMisconduct", "Cyber" })
        {
            var has = HasCoverageFlag(coverageLineNames, flag) || ScoringHelpers.ParseYesNo(TryGet(responses, $"Has{flag}Coverage"));
            if (has)
            {
                score += 20;
                factors.Add(new ScoringFactor($"HasHighRiskCoverage:{flag}", "true", 20m));
            }
            else
            {
                factors.Add(new ScoringFactor($"HasHighRiskCoverage:{flag}", "false", 0m));
            }
        }

        var multiState = ScoringHelpers.ParseYesNo(TryGet(responses, "MultiStateOperations"));
        if (multiState)
        {
            score += 15;
            factors.Add(new ScoringFactor("MultiStateOperations", "true", 15m));
        }
        else
        {
            factors.Add(new ScoringFactor("MultiStateOperations", "false", 0m));
        }

        var intl = ScoringHelpers.ParseYesNo(TryGet(responses, "InternationalExposure"));
        if (intl)
        {
            score += 20;
            factors.Add(new ScoringFactor("InternationalExposure", "true", 20m));
        }
        else
        {
            factors.Add(new ScoringFactor("InternationalExposure", "false", 0m));
        }

        score = Clamp0To100(score);
        return new ScoreWithFactors
        {
            Score = score,
            Label = LabelRiskHigh(score),
            Factors = factors
        };
    }

    private static ScoreWithFactors ScoreLitigationExposure(
        LossRunSummary loss,
        IReadOnlyList<string> coverageLineNames,
        IDictionary<string, string?> responses
    )
    {
        var score = 0;
        var factors = new List<ScoringFactor>();

        if (loss.HasLitigatedClaims)
        {
            score += 30;
            factors.Add(new ScoringFactor("HasLitigatedClaimsInHistory", "true", 30m));
        }
        else
        {
            factors.Add(new ScoringFactor("HasLitigatedClaimsInHistory", "false", 0m));
        }

        var state = (TryGet(responses, "State") ?? TryGet(responses, "PrimaryState") ?? "").Trim().ToUpperInvariant();
        var highLit = state is "CA" or "FL" or "NY" or "IL";
        if (highLit)
        {
            score += 20;
            factors.Add(new ScoringFactor("OperatesInHighLitigationState", state, 20m));
        }
        else
        {
            factors.Add(new ScoringFactor("OperatesInHighLitigationState", state, 0m));
        }

        var hasSexMis = HasCoverageFlag(coverageLineNames, "SexualMisconduct")
                        || ScoringHelpers.ParseYesNo(TryGet(responses, "HasSexualMisconductCoverage"));
        if (hasSexMis)
        {
            score += 15;
            factors.Add(new ScoringFactor("HasSexualMisconductCoverage", "true", 15m));
        }
        else
        {
            factors.Add(new ScoringFactor("HasSexualMisconductCoverage", "false", 0m));
        }

        var hasEll = HasCoverageFlag(coverageLineNames, "ELL") || HasCoverageFlag(coverageLineNames, "Educators Legal Liability");
        if (hasEll)
        {
            factors.Add(new ScoringFactor("HasELL", "true", 0m));
        }
        else
        {
            factors.Add(new ScoringFactor("HasELL", "false", 0m));
        }

        var studentBody = TryGetInt(responses, "StudentBody") + TryGetInt(responses, "StudentEnrollment");
        if (studentBody > 5000)
        {
            score += 10;
            factors.Add(new ScoringFactor("HasStudentBodyOver5000", studentBody.ToString(), 10m));
        }
        else
        {
            factors.Add(new ScoringFactor("HasStudentBodyOver5000", studentBody.ToString(), 0m));
        }

        score = Clamp0To100(score);
        return new ScoreWithFactors
        {
            Score = score,
            Label = LabelRiskHigh(score),
            Factors = factors
        };
    }

    private static int YesNoFactor(IDictionary<string, string?> responses, string key, int points, List<ScoringFactor> factors)
    {
        var yes = ScoringHelpers.ParseYesNo(TryGet(responses, key));
        if (yes)
        {
            factors.Add(new ScoringFactor(key, "yes", points));
            return points;
        }

        factors.Add(new ScoringFactor(key, "no", 0m));
        return 0;
    }

    private static int TryGetInt(IDictionary<string, string?> responses, string key)
    {
        var raw = TryGet(responses, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        if (int.TryParse(raw.Trim(), out var parsed))
        {
            return parsed;
        }

        var cleaned = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(cleaned, out var parsed2) ? parsed2 : 0;
    }

    private static string? TryGet(IDictionary<string, string?> responses, string key)
    {
        if (responses.TryGetValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    private static bool HasCoverageFlag(IReadOnlyList<string> names, string flag)
    {
        return names.Any(n => n.Contains(flag, StringComparison.OrdinalIgnoreCase));
    }

    private static int Clamp0To100(int score)
    {
        if (score < 0)
        {
            return 0;
        }

        return score > 100 ? 100 : score;
    }

    private static void ValidateScore(int score, string name)
    {
        if (score is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(name, score, "Score must be between 0 and 100.");
        }
    }

    private static string LabelPositiveHigh(int score)
    {
        return score >= 80 ? "High" : score >= 50 ? "Medium" : "Low";
    }

    private static string LabelRiskHigh(int score)
    {
        return score >= 67 ? "High" : score >= 34 ? "Medium" : "Low";
    }
}

