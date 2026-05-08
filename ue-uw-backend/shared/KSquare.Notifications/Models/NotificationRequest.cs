namespace KSquare.Notifications.Models;

public record NotificationRequest
{
    public required string EventType { get; init; }
    public required IReadOnlyList<NotificationRecipient> Recipients { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? HtmlBody { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public string? ActionUrl { get; init; }
    public string? CorrelationId { get; init; }
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
}
