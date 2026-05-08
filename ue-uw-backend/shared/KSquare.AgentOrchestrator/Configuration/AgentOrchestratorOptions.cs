namespace KSquare.AgentOrchestrator.Configuration;

public sealed class AgentOrchestratorOptions
{
    public string FunctionBaseUrl { get; set; } = "http://localhost:7071/";
    public string? FunctionKey { get; set; }
    public string ChatRoute { get; set; } = "api/assistant/chat";
    public string FeedbackRoute { get; set; } = "api/assistant/feedback";
}

