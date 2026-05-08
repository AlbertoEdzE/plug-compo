namespace KSquare.RatingAdapter.Models;

public record CoverageLineRequest
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required decimal RequestedLimit { get; init; }
    public required decimal RequestedRetention { get; init; }
    public decimal? RequestedAggregateLimit { get; init; }
}

