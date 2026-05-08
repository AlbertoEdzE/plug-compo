using KSquare.Correlation.Contracts;
using KSquare.Notifications.Configuration;
using KSquare.Notifications.Contracts;
using KSquare.Notifications.Database;
using KSquare.Notifications.Models;
using KSquare.PiiRedaction.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KSquare.Notifications.Internal;

internal sealed class NotificationDispatcher(
    NotificationOptions options,
    IEnumerable<INotificationChannel> channels,
    DedupService dedup,
    NotificationDbContext db,
    ILogger<NotificationDispatcher> logger,
    IPiiRedactor piiRedactor,
    ICorrelationContextAccessor? correlationAccessor = null
) : INotificationDispatcher
{
    private readonly IReadOnlyDictionary<string, INotificationChannel> _channels = channels
        .GroupBy(c => c.ChannelName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public async Task DispatchAsync(NotificationRequest request, CancellationToken ct = default)
    {
        if (request.Recipients.Count == 0)
        {
            return;
        }

        var correlationId = request.CorrelationId ?? correlationAccessor?.Current?.CorrelationId;
        var normalizedRequest = correlationId is null ? request : request with { CorrelationId = correlationId };
        var now = DateTimeOffset.UtcNow;

        foreach (var recipient in normalizedRequest.Recipients)
        {
            ct.ThrowIfCancellationRequested();

            var channelNames = SelectChannels(normalizedRequest, recipient);
            if (channelNames.Count == 0)
            {
                continue;
            }

            try
            {
                var acquired = await dedup.TryAcquireAsync(
                    normalizedRequest.EventType,
                    normalizedRequest.ResourceId,
                    recipient.UserId,
                    now,
                    ct
                );

                if (!acquired)
                {
                    logger.LogDebug(
                        "Skipping duplicate notification for userId={UserId}, eventType={EventType}, resourceId={ResourceId}",
                        recipient.UserId,
                        normalizedRequest.EventType,
                        normalizedRequest.ResourceId
                    );
                    continue;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Dedup check failed for userId={UserId}, eventType={EventType}, resourceId={ResourceId}",
                    recipient.UserId,
                    normalizedRequest.EventType,
                    normalizedRequest.ResourceId
                );
            }

            foreach (var channelName in channelNames)
            {
                if (!_channels.TryGetValue(channelName, out var channel))
                {
                    logger.LogWarning(
                        "Unknown notification channel {Channel} for eventType={EventType}; falling back to defaults",
                        channelName,
                        normalizedRequest.EventType
                    );
                    continue;
                }

                try
                {
                    await channel.SendAsync(normalizedRequest, recipient, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Notification channel {Channel} failed for userId={UserId}, eventType={EventType}, title={Title}",
                        channelName,
                        recipient.UserId,
                        normalizedRequest.EventType,
                        piiRedactor.RedactValue(normalizedRequest.Title)
                    );
                }
            }
        }
    }

    public async Task MarkReadAsync(string userId, Guid? notificationId = null, CancellationToken ct = default)
    {
        if (!options.EnableInApp)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            IQueryable<InAppNotificationRecord> query = db.InAppNotifications.Where(n => n.UserId == userId);
            if (notificationId is not null)
            {
                query = query.Where(n => n.NotificationId == notificationId.Value);
            }

            var records = await query.ToListAsync(ct);
            if (records.Count == 0)
            {
                return;
            }

            foreach (var record in records.Where(r => !r.IsRead))
            {
                record.IsRead = true;
                record.ReadAt = now;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark notifications as read for userId={UserId}", userId);
        }
    }

    public async IAsyncEnumerable<InAppNotification> GetInAppAsync(string userId, int limit = 50, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!options.EnableInApp)
        {
            yield break;
        }

        var effectiveLimit = limit <= 0 ? 50 : Math.Min(limit, 200);

        List<InAppNotificationRecord> rows;
        try
        {
            rows = await db.InAppNotifications.AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(effectiveLimit)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to query in-app notifications for userId={UserId}", userId);
            yield break;
        }

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            yield return new InAppNotification
            {
                NotificationId = row.NotificationId,
                UserId = row.UserId,
                EventType = row.EventType,
                Title = row.Title,
                Body = row.Body,
                ActionUrl = row.ActionUrl,
                ResourceType = row.ResourceType,
                ResourceId = row.ResourceId,
                IsRead = row.IsRead,
                CreatedAt = row.CreatedAt,
                ReadAt = row.ReadAt
            };
        }
    }

    private IReadOnlyList<string> SelectChannels(NotificationRequest request, NotificationRecipient recipient)
    {
        var selected = (recipient.OverrideChannels is { Count: > 0 }
                ? recipient.OverrideChannels
                : GetDefaultChannels(request.Priority))
            .Select(Normalize)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Where(IsEnabled)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return selected;
    }

    private IEnumerable<string> GetDefaultChannels(NotificationPriority priority)
    {
        return priority == NotificationPriority.Critical
            ? options.DefaultChannelsCritical
            : options.DefaultChannelsNormal;
    }

    private bool IsEnabled(string channelName)
    {
        return channelName.ToLowerInvariant() switch
        {
            "email" => options.EnableEmail,
            "inapp" => options.EnableInApp,
            "sms" => options.EnableSms,
            "teams" => options.EnableTeams,
            _ => true
        };
    }

    private static string Normalize(string channelName) => channelName.Trim().ToLowerInvariant();
}
