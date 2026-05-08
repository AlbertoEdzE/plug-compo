namespace KSquare.PolicyAdminAdapter.Models;

public sealed record BindCoverageLine
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required decimal Limit { get; init; }
    public required decimal Retention { get; init; }
    public required decimal AnnualPremium { get; init; }
    public decimal? AggregateLimit { get; init; }
    public string? CoverageConditions { get; init; }
}

