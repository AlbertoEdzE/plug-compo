using System.Text.Json.Serialization;

namespace KSquare.DocumentNarrative.Models;

public sealed record NarrativeRequest
{
    [JsonPropertyName("submission_id")]
    public required string SubmissionId { get; init; }

    [JsonPropertyName("narrative_type")]
    public NarrativeType NarrativeType { get; init; }

    [JsonPropertyName("submission_context")]
    public required SubmissionContext SubmissionContext { get; init; }

    [JsonPropertyName("loss_history")]
    public LossHistoryContext? LossHistory { get; init; }

    [JsonPropertyName("underwriter_name")]
    public string? UnderwriterName { get; init; }

    [JsonPropertyName("additional_notes")]
    public string? AdditionalNotes { get; init; }

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }
}
