using System.Text.Json.Serialization;

namespace KSquare.DocumentNarrative.Models;

public sealed record NarrativeResult
{
    [JsonPropertyName("submission_id")]
    public required string SubmissionId { get; init; }

    [JsonPropertyName("narrative_type")]
    public NarrativeType NarrativeType { get; init; }

    [JsonPropertyName("narrative_text")]
    public required string NarrativeText { get; init; }

    [JsonPropertyName("sections")]
    public Dictionary<string, string> Sections { get; init; } = new();

    [JsonPropertyName("word_count")]
    public int WordCount { get; init; }

    [JsonPropertyName("model_version")]
    public required string ModelVersion { get; init; }

    [JsonPropertyName("prompt_version")]
    public required string PromptVersion { get; init; }

    [JsonPropertyName("latency_ms")]
    public int LatencyMs { get; init; }

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }
}
