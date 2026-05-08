using System.Text.Json.Serialization;

namespace KSquare.IntelligentPrefill.Models;

public sealed record PrefillResult
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("field_results")]
    public IReadOnlyList<PrefillFieldResult> FieldResults { get; init; } = Array.Empty<PrefillFieldResult>();

    [JsonPropertyName("total_fields_requested")]
    public int TotalFieldsRequested { get; init; }

    [JsonPropertyName("total_fields_filled")]
    public int TotalFieldsFilled { get; init; }

    [JsonPropertyName("total_needs_review")]
    public int TotalNeedsReview { get; init; }

    [JsonPropertyName("model_version")]
    public required string ModelVersion { get; init; }

    [JsonPropertyName("prompt_version")]
    public required string PromptVersion { get; init; }

    [JsonPropertyName("latency_ms")]
    public int LatencyMs { get; init; }

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }
}
