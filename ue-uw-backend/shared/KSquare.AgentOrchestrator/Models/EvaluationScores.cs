namespace KSquare.AgentOrchestrator.Models;

public sealed record EvaluationScores(
    double? Groundedness = null,
    double? AnswerRelevance = null,
    double? ContextRelevance = null,
    double? Faithfulness = null,
    int? LatencyMs = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    double? EstimatedCostUsd = null
);

