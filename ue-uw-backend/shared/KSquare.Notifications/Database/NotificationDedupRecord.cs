namespace KSquare.Notifications.Database;

public sealed class NotificationDedupRecord
{
    public string DedupKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
}
