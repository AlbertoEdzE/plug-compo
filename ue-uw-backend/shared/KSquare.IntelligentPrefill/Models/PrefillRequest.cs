using System.Text.Json.Serialization;

namespace KSquare.IntelligentPrefill.Models;

public sealed record PrefillRequest
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("document_text")]
    public required string DocumentText { get; init; }

    [JsonPropertyName("document_type")]
    public required string DocumentType { get; init; }

    [JsonPropertyName("unmapped_fields")]
    public IReadOnlyList<UnmappedField> UnmappedFields { get; init; } = Array.Empty<UnmappedField>();

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }
}
