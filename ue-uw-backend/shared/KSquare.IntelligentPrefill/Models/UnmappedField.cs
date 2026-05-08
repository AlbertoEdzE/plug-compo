using System.Text.Json.Serialization;

namespace KSquare.IntelligentPrefill.Models;

public sealed record UnmappedField
{
    [JsonPropertyName("canonical_field")]
    public required string CanonicalField { get; init; }

    [JsonPropertyName("display_label")]
    public required string DisplayLabel { get; init; }

    [JsonPropertyName("expected_type")]
    public required string ExpectedType { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}
