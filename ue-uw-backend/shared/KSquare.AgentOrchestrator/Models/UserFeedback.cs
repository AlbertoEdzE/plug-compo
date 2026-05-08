namespace KSquare.AgentOrchestrator.Models;

public sealed record UserFeedback(
    string SessionId,
    string TurnId,
    string UserId,
    string Rating,
    string? Comment = null
);

