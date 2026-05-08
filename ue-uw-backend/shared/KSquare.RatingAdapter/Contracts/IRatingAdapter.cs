using KSquare.RatingAdapter.Models;

namespace KSquare.RatingAdapter.Contracts;

public interface IRatingAdapter
{
    Task<RatingResult> RequestPricingAsync(CoveragePricingRequest request, CancellationToken ct = default);
}

