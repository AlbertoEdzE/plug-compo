namespace KSquare.RatingAdapter.Models;

public record CoverageLinePremium
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required decimal AnnualPremium { get; init; }
    public decimal? MinimumPremium { get; init; }
    public decimal? SurchargeAmount { get; init; }
    public decimal? CreditAmount { get; init; }
    public IReadOnlyList<RatingFactor> Factors { get; init; } = [];
}

