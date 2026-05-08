namespace KSquare.PolicyAdminAdapter.Models;

public sealed record Address
{
    public required string Line1 { get; init; }
    public string? Line2 { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string Zip { get; init; }
}

