namespace KSquare.AgentOrchestrator.Models;

public sealed record AgentChatRequest(
    string SessionId,
    string SubmissionId,
    string UserId,
    string UserRole,
    IReadOnlyList<ChatMessage> Messages,
    string? CorrelationId = null
);

