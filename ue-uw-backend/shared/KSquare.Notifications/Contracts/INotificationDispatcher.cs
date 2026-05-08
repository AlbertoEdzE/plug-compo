using KSquare.Notifications.Models;

namespace KSquare.Notifications.Contracts;

public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationRequest request, CancellationToken ct = default);
    Task MarkReadAsync(string userId, Guid? notificationId = null, CancellationToken ct = default);
    IAsyncEnumerable<InAppNotification> GetInAppAsync(string userId, int limit = 50, CancellationToken ct = default);
}
