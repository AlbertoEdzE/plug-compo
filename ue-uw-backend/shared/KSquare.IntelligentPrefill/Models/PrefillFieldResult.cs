using System.Text.Json.Serialization;

namespace KSquare.IntelligentPrefill.Models;

public sealed record PrefillFieldResult
{
    [JsonPropertyName("canonical_field")]
    public required string CanonicalField { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("source_text")]
    public required string SourceText { get; init; }

    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }

    [JsonPropertyName("needs_review")]
    public bool NeedsReview { get; init; }
}
