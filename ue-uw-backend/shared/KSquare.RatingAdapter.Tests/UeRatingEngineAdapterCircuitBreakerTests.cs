using FluentAssertions;
using KSquare.Correlation.Extensions;
using KSquare.RatingAdapter.Configuration;
using KSquare.RatingAdapter.Contracts;
using KSquare.RatingAdapter.Extensions;
using KSquare.RatingAdapter.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KSquare.RatingAdapter.Tests;

public sealed class UeRatingEngineAdapterCircuitBreakerTests
{
    [Fact]
    public async Task Circuit_breaker_opens_after_5_consecutive_failures()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/v2/rate").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("""{ "error": "server error" }"""));

        var services = new ServiceCollection();
        services.AddKsCorrelation();
        services.AddKsRatingAdapter(o =>
        {
            o.Provider = RatingProvider.UeRatingEngine;
            o.RatingEngineBaseUrl = server.Url!;
            o.RatingEngineApiKey = "test";
            o.RatingEngineVersion = "v2";
            o.MaxRetryAttempts = 0;
            o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            o.CircuitBreakerFailureThreshold = 5;
            o.CircuitBreakerResetTimeout = TimeSpan.FromMinutes(1);
            o.RequestTimeout = TimeSpan.FromSeconds(5);
        });

        var sp = services.BuildServiceProvider();
        var adapter = sp.GetRequiredService<IRatingAdapter>();

        var request = BuildRequest();

        for (var i = 0; i < 5; i++)
        {
            var result = await adapter.RequestPricingAsync(request);
            result.Status.Should().Be(RatingStatus.RatingFailed);
        }

        var sixth = await adapter.RequestPricingAsync(request);
        sixth.Status.Should().Be(RatingStatus.RatingFailed);
        sixth.Messages.Should().Contain(m => m.Code == "CIRCUIT_OPEN");

        server.LogEntries.Count(e => e.RequestMessage.Method == "POST" && e.RequestMessage.Path == "/api/v2/rate").Should().Be(5);
    }

    private static CoveragePricingRequest BuildRequest()
    {
        return new CoveragePricingRequest
        {
            SubmissionId = "sub-1",
            QuoteId = "quote-1",
            InstitutionType = "K-12 Public District",
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
                new CoverageLineRequest { ProductCode = "GL", ProductName = "General Liability", RequestedLimit = 1_000_000m, RequestedRetention = 10_000m }
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

