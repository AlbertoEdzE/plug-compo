using Microsoft.EntityFrameworkCore;

namespace KSquare.Notifications.Database;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<InAppNotificationRecord> InAppNotifications => Set<InAppNotificationRecord>();
    public DbSet<NotificationDedupRecord> NotificationDedup => Set<NotificationDedupRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InAppNotificationRecord>(entity =>
        {
            entity.ToTable("in_app_notifications");
            entity.HasKey(x => x.NotificationId);

            entity.Property(x => x.NotificationId).HasColumnName("notification_id");
            entity.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(500);
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(200);
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
            entity.Property(x => x.Body).HasColumnName("body");
            entity.Property(x => x.ActionUrl).HasColumnName("action_url").HasMaxLength(1000);
            entity.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(100);
            entity.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(500);
            entity.Property(x => x.IsRead).HasColumnName("is_read");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ReadAt).HasColumnName("read_at");

            entity.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt }).HasDatabaseName("IX_notif_user_unread");
            entity.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_notif_created");
        });

        modelBuilder.Entity<NotificationDedupRecord>(entity =>
        {
            entity.ToTable("notification_dedup");
            entity.HasKey(x => x.DedupKey);

            entity.Property(x => x.DedupKey).HasColumnName("dedup_key").HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        });
    }
}
