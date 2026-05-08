using KSquare.RatingAdapter.Contracts;
using KSquare.RatingAdapter.Models;

namespace KSquare.RatingAdapter.Providers;

public sealed class MockRatingAdapter : IRatingAdapter
{
    public Task<RatingResult> RequestPricingAsync(CoveragePricingRequest request, CancellationToken ct = default)
    {
        var lines = new List<CoverageLinePremium>(request.CoverageLines.Count);
        var messages = new List<RatingMessage>();

        foreach (var line in request.CoverageLines)
        {
            var premium = CalculatePremium(line.ProductCode, request);
            if (premium is null)
            {
                messages.Add(new RatingMessage(RatingMessageLevel.Warning, "UNKNOWN_LINE", $"Unknown product code '{line.ProductCode}'."));
                premium = 0m;
            }

            lines.Add(new CoverageLinePremium
            {
                ProductCode = line.ProductCode,
                ProductName = line.ProductName,
                AnnualPremium = premium.Value
            });
        }

        var total = lines.Sum(l => l.AnnualPremium);

        return Task.FromResult(new RatingResult
        {
            SubmissionId = request.SubmissionId,
            QuoteId = request.QuoteId,
            Status = RatingStatus.Rated,
            PremiumLines = lines,
            TotalAnnualPremium = total,
            RatingEngineReferenceId = "mock",
            RatingBasis = "Mock",
            Messages = messages,
            CorrelationId = request.CorrelationId
        });
    }

    private static decimal? CalculatePremium(string productCode, CoveragePricingRequest request)
    {
        return productCode switch
        {
            "GL" => request.TotalInsuredValue * 0.00068m,
            "PROP" => request.TotalInsuredValue * 0.00122m,
            "ELL" => request.TotalInsuredValue * 0.00047m,
            "SA" => request.TotalEnrollment * 12.80m,
            _ => null
        };
    }
}

