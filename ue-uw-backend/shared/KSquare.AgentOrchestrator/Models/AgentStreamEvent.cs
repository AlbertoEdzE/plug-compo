namespace KSquare.AgentOrchestrator.Models;

public sealed record AgentStreamEvent(
    string Type,
    string? RunId = null,
    string? Delta = null,
    ToolCallEvent? Tool = null,
    EvaluationScores? Eval = null,
    string? Error = null,
    bool? Done = null
);

