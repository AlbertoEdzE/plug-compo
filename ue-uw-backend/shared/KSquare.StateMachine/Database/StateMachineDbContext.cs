using Microsoft.EntityFrameworkCore;

namespace KSquare.StateMachine.Database;

public sealed class StateMachineDbContext(DbContextOptions<StateMachineDbContext> options) : DbContext(options)
{
    public DbSet<StateRecordEntity> StateRecords => Set<StateRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StateRecordEntity>(entity =>
        {
            entity.ToTable("state_records");
            entity.HasKey(x => new { x.EntityType, x.EntityId });

            entity.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100);
            entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(64);
            entity.Property(x => x.CurrentState).HasColumnName("current_state").HasMaxLength(100);
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);

            entity.HasIndex(x => new { x.EntityType, x.CurrentState }).HasDatabaseName("IX_state_records_type_state");
        });
    }
}

