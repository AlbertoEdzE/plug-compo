namespace KSquare.RulesEngine.Models;

public sealed record RuleResult
{
    public required string RuleName { get; init; }
    public required bool Fired { get; init; }
    public required string? Action { get; init; }
    public string? Reason { get; init; }
    public IDictionary<string, object?> MatchedFacts { get; init; } = new Dictionary<string, object?>();
}

