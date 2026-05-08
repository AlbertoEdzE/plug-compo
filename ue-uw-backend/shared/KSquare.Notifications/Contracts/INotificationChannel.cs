using KSquare.Notifications.Models;

namespace KSquare.Notifications.Contracts;

public interface INotificationChannel
{
    string ChannelName { get; }
    Task SendAsync(NotificationRequest request, NotificationRecipient recipient, CancellationToken ct);
}
