using FluentAssertions;
using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Extensions;
using KSquare.RiskAnalysis.Models;
using KSquare.RulesEngine.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.RiskAnalysis.Tests;

public sealed class AppetiteCalculatorTests
{
    [Fact]
    public async Task Returns_zero_for_out_of_appetite_naics()
    {
        using var sp = BuildProvider();
        using var scope = sp.CreateScope();
        var calculator = scope.ServiceProvider.GetRequiredService<IAppetiteCalculator>();
        var scores = GoodScores();

        var baseCtx = BaseContext();
        var ctx = baseCtx with
        {
            NaicsCode = "713210",
            LossRunSummary = baseCtx.LossRunSummary with { FiveYearAverageLossRatio = 0.10m }
        };

        var result = await calculator.CalculateAsync(scores, ctx);
        result.Score.Should().Be(0.0f);
        result.Classification.Should().Be("Out of Appetite");
    }

    [Fact]
    public async Task Returns_in_appetite_for_low_risk_submission()
    {
        using var sp = BuildProvider();
        using var scope = sp.CreateScope();
        var calculator = scope.ServiceProvider.GetRequiredService<IAppetiteCalculator>();
        var scores = GoodScores();
        var baseCtx = BaseContext();
        var ctx = baseCtx with
        {
            NaicsCode = "5311",
            LossRunSummary = baseCtx.LossRunSummary with { FiveYearAverageLossRatio = 0.20m }
        };

        var result = await calculator.CalculateAsync(scores, ctx);
        result.Classification.Should().Be("In Appetite");
        result.Score.Should().BeGreaterThan(0.80f);
    }

    [Fact]
    public async Task High_tiv_and_high_loss_ratio_declines()
    {
        using var sp = BuildProvider();
        using var scope = sp.CreateScope();
        var calculator = scope.ServiceProvider.GetRequiredService<IAppetiteCalculator>();
        var scores = GoodScores();

        var baseCtx = BaseContext();
        var ctx = baseCtx with
        {
            TotalInsuredValue = 75_000_000m,
            LossRunSummary = baseCtx.LossRunSummary with { FiveYearAverageLossRatio = 0.81m }
        };

        var result = await calculator.CalculateAsync(scores, ctx);
        result.Score.Should().Be(0.0f);
        result.Classification.Should().Be("Out of Appetite");
    }

    [Fact]
    public void Composite_score_matches_hand_calculated_value()
    {
        var scores = new RiskIndicatorScores
        {
            CampusSafetyRating = new ScoreWithFactors { Score = 88, Label = "High", Factors = Array.Empty<ScoringFactor>() },
            ClaimsSeverity = new ScoreWithFactors { Score = 34, Label = "Medium", Factors = Array.Empty<ScoringFactor>() },
            PolicyComplexity = new ScoreWithFactors { Score = 61, Label = "Medium", Factors = Array.Empty<ScoringFactor>() },
            LitigationExposure = new ScoreWithFactors { Score = 22, Label = "Low", Factors = Array.Empty<ScoringFactor>() }
        };

        var expected = 88m * 0.30m + (100m - 34m) * 0.30m + (100m - 61m) * 0.20m + (100m - 22m) * 0.20m;
        scores.CompositeRiskScore.Should().Be(expected);
    }

    private static RiskIndicatorScores GoodScores()
    {
        return new RiskIndicatorScores
        {
            CampusSafetyRating = new ScoreWithFactors { Score = 90, Label = "High", Factors = Array.Empty<ScoringFactor>() },
            ClaimsSeverity = new ScoreWithFactors { Score = 10, Label = "Low", Factors = Array.Empty<ScoringFactor>() },
            PolicyComplexity = new ScoreWithFactors { Score = 10, Label = "Low", Factors = Array.Empty<ScoringFactor>() },
            LitigationExposure = new ScoreWithFactors { Score = 10, Label = "Low", Factors = Array.Empty<ScoringFactor>() }
        };
    }

    private static RiskScoringContext BaseContext()
    {
        return new RiskScoringContext
        {
            SubmissionId = "sub",
            InstitutionType = "K-12 Public District",
            NaicsCode = "5311",
            NumberOfLocations = 2,
            TotalInsuredValue = 10_000_000m,
            NumberOfCoverageLines = 2,
            CoverageLineNames = new[] { "GL", "PROP" },
            FormResponses = new Dictionary<string, string?>(),
            LossRunSummary = new LossRunSummary
            {
                AnnualRecords = Array.Empty<AnnualLossRecord>(),
                FiveYearAverageLossRatio = 0.20m,
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

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddKsRulesEngine(_ => { }).AddRuleSet("appetite-scoring");
        services.AddKsRiskAnalysis(options =>
        {
            options.OutOfAppetiteNaicsCodes = new List<string> { "713210", "722511" };
        });

        return services.BuildServiceProvider();
    }
}
