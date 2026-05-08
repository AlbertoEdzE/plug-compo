using FluentAssertions;
using KSquare.RiskAnalysis.Internal;
using KSquare.RiskAnalysis.Tests.Synthesizers;

namespace KSquare.RiskAnalysis.Tests;

public sealed class LossRunTableParserTests
{
    [Fact]
    public void Parses_year_claims_incurred_and_ratio()
    {
        var synth = new LossRunTableSynthesizer(seed: 1);
        var table = synth.Table(
            headers: new[] { "Policy Year", "Claim Count", "Paid + Reserved", "Loss Ratio" },
            rows: new[]
            {
                new string?[] { "2022", "3", "$12,345", "21%" }
            }
        );

        var parser = new LossRunTableParser();
        var records = parser.Parse(table);

        records.Should().HaveCount(1);
        records[0].Year.Should().Be(2022);
        records[0].ClaimsCount.Should().Be(3);
        records[0].TotalIncurred.Should().Be(12345m);
        records[0].LossRatio.Should().BeApproximately(0.21m, 0.0001m);
    }

    [Fact]
    public void Skips_total_and_average_rows()
    {
        var synth = new LossRunTableSynthesizer(seed: 2);
        var table = synth.Table(
            headers: new[] { "Year of Loss", "Claims", "Incurred", "L/R" },
            rows: new[]
            {
                new string?[] { "2021", "1", "1000", "10%" },
                new string?[] { "Total", "999", "999", "999%" },
                new string?[] { "5-Year Average", "2", "2000", "20%" }
            }
        );

        var parser = new LossRunTableParser();
        var records = parser.Parse(table);

        records.Should().HaveCount(1);
        records[0].Year.Should().Be(2021);
    }
}

