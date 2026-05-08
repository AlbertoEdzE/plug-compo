using FluentAssertions;
using KSquare.RatingAdapter.Models;
using KSquare.RatingAdapter.Providers;

namespace KSquare.RatingAdapter.Tests;

public sealed class MockRatingAdapterTests
{
    [Fact]
    public async Task Mock_adapter_returns_deterministic_premiums()
    {
        var adapter = new MockRatingAdapter();
        var request = BuildRequest();

        var first = await adapter.RequestPricingAsync(request);
        var second = await adapter.RequestPricingAsync(request);

        first.TotalAnnualPremium.Should().Be(second.TotalAnnualPremium);
        first.PremiumLines.Select(l => l.AnnualPremium).Should().Equal(second.PremiumLines.Select(l => l.AnnualPremium));
    }

    [Fact]
    public async Task Mock_adapter_gl_formula_matches_spec()
    {
        var adapter = new MockRatingAdapter();
        var request = BuildRequest(totalInsuredValue: 10_000_000m);

        var result = await adapter.RequestPricingAsync(request);
        var gl = result.PremiumLines.Single(l => l.ProductCode == "GL");

        gl.AnnualPremium.Should().Be(10_000_000m * 0.00068m);
    }

    [Fact]
    public async Task Mock_adapter_total_equals_sum_of_line_premiums()
    {
        var adapter = new MockRatingAdapter();
        var request = BuildRequest(totalInsuredValue: 5_000_000m, totalEnrollment: 1200);

        var result = await adapter.RequestPricingAsync(request);

        result.TotalAnnualPremium.Should().Be(result.PremiumLines.Sum(l => l.AnnualPremium));
    }

    private static CoveragePricingRequest BuildRequest(decimal totalInsuredValue = 2_500_000m, int totalEnrollment = 800)
    {
        return new CoveragePricingRequest
        {
            SubmissionId = "sub-1",
            QuoteId = "quote-1",
            InstitutionType = "K-12 Public District",
            NaicsCode = "611110",
            State = "NY",
            NumberOfLocations = 3,
            TotalEnrollment = totalEnrollment,
            FteEmployees = 150,
            TotalInsuredValue = totalInsuredValue,
            OperatingExpenses = 1_000_000m,
            EffectiveDate = new DateOnly(2026, 1, 1),
            ExpirationDate = new DateOnly(2027, 1, 1),
            CoverageLines = new[]
            {
                new CoverageLineRequest { ProductCode = "GL", ProductName = "General Liability", RequestedLimit = 1_000_000m, RequestedRetention = 10_000m },
                new CoverageLineRequest { ProductCode = "PROP", ProductName = "Property", RequestedLimit = 10_000_000m, RequestedRetention = 25_000m },
                new CoverageLineRequest { ProductCode = "ELL", ProductName = "Umbrella", RequestedLimit = 5_000_000m, RequestedRetention = 0m },
                new CoverageLineRequest { ProductCode = "SA", ProductName = "Student Accident", RequestedLimit = 0m, RequestedRetention = 0m }
            },
            LossHistory = new LossHistorySummary
            {
                FiveYearAverageLossRatio = 0.25m,
                LargestSingleLoss = 250_000m,
                TotalClaimsCount = 12,
                DataYearsAvailable = 5
            },
            CorrelationId = "corr-1"
        };
    }
}

