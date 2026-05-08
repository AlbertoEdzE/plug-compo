using System.Text.Json;
using FluentAssertions;
using KSquare.RatingAdapter.Mapping;
using KSquare.RatingAdapter.Models;

namespace KSquare.RatingAdapter.Tests;

public sealed class UeRatingEnginePricingMapperTests
{
    [Fact]
    public void MapToRatingInput_maps_institution_type_k12_to_k12_public()
    {
        var mapper = new UeRatingEnginePricingMapper();
        var input = mapper.MapToRatingInput(BuildRequest(institutionType: "K-12 Public District"));

        var json = JsonSerializer.Serialize(input.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("institutionType").GetString().Should().Be("K12_PUBLIC");
    }

    [Fact]
    public void MapToRatingInput_maps_coverage_lines_correctly()
    {
        var mapper = new UeRatingEnginePricingMapper();
        var input = mapper.MapToRatingInput(BuildRequest());

        var json = JsonSerializer.Serialize(input.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        var lines = doc.RootElement.GetProperty("coverageLines");
        lines.GetArrayLength().Should().Be(3);

        lines[0].GetProperty("lineCode").GetString().Should().Be("GL");
        lines[0].GetProperty("limit").GetDecimal().Should().Be(1_000_000m);
        lines[0].GetProperty("retention").GetDecimal().Should().Be(10_000m);

        lines[1].GetProperty("lineCode").GetString().Should().Be("PROP");
        lines[2].GetProperty("lineCode").GetString().Should().Be("ELL");
    }

    [Fact]
    public void MapFromRatingOutput_extracts_total_annual_premium_as_sum()
    {
        var mapper = new UeRatingEnginePricingMapper();
        var output = BuildOutput("""
            {
              "ratingReferenceId": "ref-1",
              "ratingBasis": "Manual",
              "premiumLines": [
                { "lineCode": "GL", "lineName": "General Liability", "annualPremium": 1000.00, "minimumPremium": 500.00, "surcharge": 50.00, "credit": 0.00 },
                { "lineCode": "PROP", "lineName": "Property", "annualPremium": 2500.00 }
              ]
            }
            """);

        var result = mapper.MapFromRatingOutput(output, "corr-1");

        result.Status.Should().Be(RatingStatus.Rated);
        result.TotalAnnualPremium.Should().Be(3500m);
        result.PremiumLines.Should().HaveCount(2);
    }

    [Fact]
    public void MapFromRatingOutput_returns_failed_when_error_key_present()
    {
        var mapper = new UeRatingEnginePricingMapper();
        var output = BuildOutput("""{ "error": "bad request" }""", httpStatusCode: 400);

        var result = mapper.MapFromRatingOutput(output, "corr-1");

        result.Status.Should().Be(RatingStatus.RatingFailed);
        result.Messages.Should().ContainSingle(m => m.Code == "RATING_FAILED");
    }

    private static CoveragePricingRequest BuildRequest(string institutionType = "K-12 Public District")
    {
        return new CoveragePricingRequest
        {
            SubmissionId = "sub-1",
            QuoteId = "quote-1",
            InstitutionType = institutionType,
            NaicsCode = "611110",
            State = "NY",
            NumberOfLocations = 3,
            TotalEnrollment = 800,
            FteEmployees = 150,
            TotalInsuredValue = 2_500_000m,
            OperatingExpenses = 1_000_000m,
            EffectiveDate = new DateOnly(2026, 1, 1),
            ExpirationDate = new DateOnly(2027, 1, 1),
            CoverageLines = new[]
            {
                new CoverageLineRequest { ProductCode = "GL", ProductName = "General Liability", RequestedLimit = 1_000_000m, RequestedRetention = 10_000m },
                new CoverageLineRequest { ProductCode = "PROP", ProductName = "Property", RequestedLimit = 10_000_000m, RequestedRetention = 25_000m },
                new CoverageLineRequest { ProductCode = "ELL", ProductName = "Umbrella", RequestedLimit = 5_000_000m, RequestedRetention = 0m }
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

    private static RatingEngineOutput BuildOutput(string json, int httpStatusCode = 200)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new Dictionary<string, JsonElement>();
        var dict = parsed.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
        return new RatingEngineOutput(dict, httpStatusCode);
    }
}

