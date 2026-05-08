using System.Security.Cryptography;
using System.Text;
using KSquare.Notifications.Configuration;
using KSquare.Notifications.Database;
using Microsoft.EntityFrameworkCore;

namespace KSquare.Notifications.Internal;

internal sealed class DedupService(NotificationOptions options, NotificationDbContext db)
{
    public async Task<bool> TryAcquireAsync(string eventType, string resourceId, string userId, DateTimeOffset now, CancellationToken ct)
    {
        if (options.DeduplicationWindow <= TimeSpan.Zero)
        {
            return true;
        }

        var key = ComputeDedupKey(eventType, resourceId, userId, now, options.DeduplicationWindow);

        await DeleteExpiredAsync(now, ct);

        var exists = await db.NotificationDedup.AsNoTracking()
            .AnyAsync(x => x.DedupKey == key && x.ExpiresAt > now, ct);
        if (exists)
        {
            return false;
        }

        db.NotificationDedup.Add(new NotificationDedupRecord
        {
            DedupKey = key,
            CreatedAt = now,
            ExpiresAt = now.Add(options.DeduplicationWindow)
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct)
    {
        var expired = await db.NotificationDedup
            .Where(x => x.ExpiresAt <= now)
            .Take(500)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return;
        }

        db.NotificationDedup.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
    }

    public static string ComputeDedupKey(string eventType, string resourceId, string userId, DateTimeOffset now, TimeSpan window)
    {
        var windowStart = Floor(now, window);
        var input = $"{eventType}||{resourceId}||{userId}||{windowStart:O}";

        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DateTimeOffset Floor(DateTimeOffset value, TimeSpan window)
    {
        var ticks = window.Ticks;
        if (ticks <= 0)
        {
            return value;
        }

        var flooredTicks = value.UtcTicks - (value.UtcTicks % ticks);
        return new DateTimeOffset(flooredTicks, TimeSpan.Zero);
    }
}
