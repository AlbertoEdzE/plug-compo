namespace KSquare.RulesEngine.Models;

public sealed record RuleEvaluationResult
{
    public required string RuleSetName { get; init; }
    public required IReadOnlyList<RuleResult> Results { get; init; }
    public bool AnyFired => Results.Any(r => r.Fired);
    public required IReadOnlyList<string> FiredActions { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}

