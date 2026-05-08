using System.Text.Json.Serialization;

namespace KSquare.AiEmailTriage.Models;

public sealed record ExtractedEmailEntity
{
    [JsonPropertyName("field_name")]
    public required string FieldName { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("source_text")]
    public required string SourceText { get; init; }
}

