namespace KSquare.Notifications.Models;

public record InAppNotification
{
    public Guid NotificationId { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required string EventType { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? ActionUrl { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public bool IsRead { get; set; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
