namespace KSquare.RulesEngine.Models;

public sealed record RuleSet
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<Rule> Rules { get; init; }
}

