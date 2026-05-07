using KSquare.EventBus.Models;
using Microsoft.EntityFrameworkCore;

namespace KSquare.EventBus.Outbox;

public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OutboxMessage>();
        entity.ToTable("outbox_messages");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.Topic).HasColumnName("topic").HasMaxLength(256);
        entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(256);
        entity.Property(x => x.Payload).HasColumnName("payload");
        entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        entity.Property(x => x.MessageId).HasColumnName("message_id").HasMaxLength(128);
        entity.Property(x => x.Properties).HasColumnName("properties");
        entity.Property(x => x.Status).HasColumnName("status");
        entity.Property(x => x.RetryCount).HasColumnName("retry_count");
        entity.Property(x => x.LastError).HasColumnName("last_error");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.ProcessedAt).HasColumnName("processed_at");

        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.CreatedAt);
    }
}
