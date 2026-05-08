using System.Text.Json.Serialization;

namespace KSquare.AiEmailTriage.Models;

public sealed record EmailTriageRequest
{
    [JsonPropertyName("email_id")]
    public required string EmailId { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("body_text")]
    public required string BodyText { get; init; }

    [JsonPropertyName("sender_email")]
    public required string SenderEmail { get; init; }

    [JsonPropertyName("sender_name")]
    public string? SenderName { get; init; }

    [JsonPropertyName("received_at")]
    public required string ReceivedAt { get; init; }

    [JsonPropertyName("attachment_names")]
    public IReadOnlyList<string> AttachmentNames { get; init; } = Array.Empty<string>();

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }
}

