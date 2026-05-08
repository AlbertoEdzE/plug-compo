using KSquare.RatingAdapter.Models;

namespace KSquare.RatingAdapter.Contracts;

public interface ICoveragePricingMapper
{
    RatingEngineInput MapToRatingInput(CoveragePricingRequest request);
    RatingResult MapFromRatingOutput(RatingEngineOutput output, string correlationId);
}

