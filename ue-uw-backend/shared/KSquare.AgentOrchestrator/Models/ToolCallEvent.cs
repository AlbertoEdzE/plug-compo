namespace KSquare.AgentOrchestrator.Models;

public sealed record ToolCallEvent(
    string ToolName,
    IDictionary<string, object?> Arguments,
    string? Result = null,
    string? Error = null,
    int? DurationMs = null
);

