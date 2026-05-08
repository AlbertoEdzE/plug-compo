using FluentAssertions;
using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Internal;
using KSquare.RiskAnalysis.Models;

namespace KSquare.RiskAnalysis.Tests;

public sealed class RiskScorerTests
{
    [Fact]
    public void Campus_safety_is_100_when_all_positive_factors_present_and_no_incidents()
    {
        IRiskScorer scorer = new RiskScorerImpl();
        var ctx = BaseContext(new Dictionary<string, string?>
        {
            ["SecurityPersonnelOnSite"] = "yes",
            ["SurveillanceCamerasInstalled"] = "yes",
            ["EmergencyResponsePlanInPlace"] = "yes",
            ["SafetyTrainingConductedAnnually"] = "yes",
            ["IncidentReportingSystem"] = "yes",
            ["CrisisManagementTeam"] = "yes",
            ["SchoolResourceOfficer"] = "yes",
            ["RecentSecurityIncidents"] = "0",
            ["TrespassingIncidents"] = "0",
        });

        var scores = scorer.Score(ctx);
        scores.CampusSafetyRating.Score.Should().Be(100);
    }

    [Fact]
    public void Campus_safety_reduced_by_recent_security_incidents()
    {
        IRiskScorer scorer = new RiskScorerImpl();
        var ctx = BaseContext(new Dictionary<string, string?>
        {
            ["SecurityPersonnelOnSite"] = "yes",
            ["SurveillanceCamerasInstalled"] = "yes",
            ["EmergencyResponsePlanInPlace"] = "yes",
            ["SafetyTrainingConductedAnnually"] = "yes",
            ["IncidentReportingSystem"] = "yes",
            ["CrisisManagementTeam"] = "yes",
            ["SchoolResourceOfficer"] = "yes",
            ["RecentSecurityIncidents"] = "4",
            ["TrespassingIncidents"] = "0",
        });

        var scores = scorer.Score(ctx);
        scores.CampusSafetyRating.Score.Should().Be(70);
    }

    [Fact]
    public void Policy_complexity_increases_with_more_coverage_lines()
    {
        IRiskScorer scorer = new RiskScorerImpl();

        var lowBase = BaseContext(new Dictionary<string, string?>());
        var low = lowBase with { NumberOfCoverageLines = 2, CoverageLineNames = new[] { "GL", "PROP" } };

        var highBase = BaseContext(new Dictionary<string, string?>());
        var high = highBase with
        {
            NumberOfCoverageLines = 6,
            CoverageLineNames = new[] { "GL", "PROP", "Cyber", "Athletic", "SexualMisconduct", "ELL" }
        };

        scorer.Score(high).PolicyComplexity.Score.Should().BeGreaterThan(scorer.Score(low).PolicyComplexity.Score);
    }

    private static RiskScoringContext BaseContext(IDictionary<string, string?> responses)
    {
        return new RiskScoringContext
        {
            SubmissionId = "sub",
            InstitutionType = "K-12 Public District",
            NaicsCode = "5311",
            NumberOfLocations = 3,
            TotalInsuredValue = 10_000_000m,
            NumberOfCoverageLines = 3,
            CoverageLineNames = new[] { "GL", "PROP", "ELL" },
            FormResponses = responses,
            LossRunSummary = new LossRunSummary
            {
                AnnualRecords = Array.Empty<AnnualLossRecord>(),
                FiveYearAverageLossRatio = 0.0m,
                TotalClaimsCount = 0,
                TotalIncurred = 0m,
                Trend = LossTrend.Insufficient,
                HasLitigatedClaims = false,
                LargestSingleLoss = null,
                DataYearsAvailable = 0
            },
            CorrelationId = null
        };
    }
}
