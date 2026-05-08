using KSquare.AgentOrchestrator.Models;

namespace KSquare.AgentOrchestrator.Contracts;

public interface IAgentOrchestratorClient
{
    IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(AgentChatRequest request, CancellationToken ct = default);
    Task SendFeedbackAsync(UserFeedback feedback, CancellationToken ct = default);
}

