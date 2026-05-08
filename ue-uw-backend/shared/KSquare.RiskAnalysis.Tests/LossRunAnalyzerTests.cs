using FluentAssertions;
using KSquare.RiskAnalysis.Configuration;
using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Internal;
using KSquare.RiskAnalysis.Models;
using KSquare.RiskAnalysis.Tests.Synthesizers;

namespace KSquare.RiskAnalysis.Tests;

public sealed class LossRunAnalyzerTests
{
    [Fact]
    public void Computes_five_year_average_loss_ratio()
    {
        var synth = new LossRunTableSynthesizer(seed: 3);
        var table = synth.Table(
            headers: new[] { "Year", "Claims", "Incurred", "Loss Ratio" },
            rows: new[]
            {
                new string?[] { "2020", "1", "1000", "10%" },
                new string?[] { "2021", "1", "1000", "20%" },
                new string?[] { "2022", "1", "1000", "30%" },
                new string?[] { "2023", "1", "1000", "40%" },
                new string?[] { "2024", "1", "1000", "50%" },
            }
        );

        ILossRunAnalyzer analyzer = new LossRunAnalyzer(new RiskAnalysisOptions { MinimumLossHistoryYears = 3 });
        var summary = analyzer.Analyze(new[] { table }, "sub-1");

        summary.FiveYearAverageLossRatio.Should().BeApproximately(0.30m, 0.0001m);
        summary.DataYearsAvailable.Should().Be(5);
    }

    [Fact]
    public void Returns_worsening_trend_when_last_two_years_exceed_prior_three_years()
    {
        var synth = new LossRunTableSynthesizer(seed: 4);
        var table = synth.Table(
            headers: new[] { "Year", "Claims", "Incurred", "Loss Ratio" },
            rows: new[]
            {
                new string?[] { "2020", "1", "1000", "10%" },
                new string?[] { "2021", "1", "1000", "10%" },
                new string?[] { "2022", "1", "1000", "10%" },
                new string?[] { "2023", "1", "1000", "20%" },
                new string?[] { "2024", "1", "1000", "20%" },
            }
        );

        ILossRunAnalyzer analyzer = new LossRunAnalyzer(new RiskAnalysisOptions { MinimumLossHistoryYears = 3 });
        var summary = analyzer.Analyze(new[] { table }, "sub-2");

        summary.Trend.Should().Be(LossTrend.Worsening);
    }
}

