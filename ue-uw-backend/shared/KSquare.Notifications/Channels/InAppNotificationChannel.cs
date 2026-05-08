using KSquare.Notifications.Contracts;
using KSquare.Notifications.Database;
using KSquare.Notifications.Models;
using KSquare.PiiRedaction.Contracts;
using Microsoft.Extensions.Logging;

namespace KSquare.Notifications.Channels;

public sealed class InAppNotificationChannel(
    NotificationDbContext db,
    ILogger<InAppNotificationChannel> logger,
    IPiiRedactor piiRedactor
) : INotificationChannel
{
    public string ChannelName => "inapp";

    public async Task SendAsync(NotificationRequest request, NotificationRecipient recipient, CancellationToken ct)
    {
        try
        {
            db.InAppNotifications.Add(new InAppNotificationRecord
            {
                NotificationId = Guid.NewGuid(),
                UserId = recipient.UserId,
                EventType = request.EventType,
                Title = request.Title,
                Body = request.Body,
                ActionUrl = request.ActionUrl,
                ResourceType = request.ResourceType,
                ResourceId = request.ResourceId,
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow,
                ReadAt = null
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "In-app notification insert failed for userId={UserId}, eventType={EventType}, title={Title}",
                recipient.UserId,
                request.EventType,
                piiRedactor.RedactValue(request.Title)
            );
        }
    }
}
