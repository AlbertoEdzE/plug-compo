namespace KSquare.AgentOrchestrator.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    string? ToolName = null
);

