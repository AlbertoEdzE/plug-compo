namespace KSquare.RulesEngine.Models;

public sealed record Rule
{
    public required string RuleName { get; init; }
    public required int Priority { get; init; }
    public string? Description { get; init; }
    public required string Condition { get; init; }
    public required string Action { get; init; }
    public string? Reason { get; init; }
}

