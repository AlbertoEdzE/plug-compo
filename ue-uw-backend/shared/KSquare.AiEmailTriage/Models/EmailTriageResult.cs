using System.Text.Json.Serialization;

namespace KSquare.AiEmailTriage.Models;

public sealed record EmailTriageResult
{
    [JsonPropertyName("email_id")]
    public required string EmailId { get; init; }

    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    [JsonPropertyName("intent_confidence")]
    public required double IntentConfidence { get; init; }

    [JsonPropertyName("extracted_entities")]
    public required IReadOnlyList<ExtractedEmailEntity> ExtractedEntities { get; init; }

    [JsonPropertyName("routing_suggestion")]
    public required string RoutingSuggestion { get; init; }

    [JsonPropertyName("urgency")]
    public required string Urgency { get; init; }

    [JsonPropertyName("urgency_signals")]
    public required IReadOnlyList<string> UrgencySignals { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("model_version")]
    public required string ModelVersion { get; init; }

    [JsonPropertyName("prompt_version")]
    public required string PromptVersion { get; init; }

    [JsonPropertyName("latency_ms")]
    public required int LatencyMs { get; init; }

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }
}

