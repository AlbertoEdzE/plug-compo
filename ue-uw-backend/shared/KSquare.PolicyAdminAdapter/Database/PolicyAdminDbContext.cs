using Microsoft.EntityFrameworkCore;

namespace KSquare.PolicyAdminAdapter.Database;

public sealed class PolicyAdminDbContext(DbContextOptions<PolicyAdminDbContext> options) : DbContext(options)
{
    public DbSet<BindJobRecord> BindJobs => Set<BindJobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BindJobRecord>(entity =>
        {
            entity.ToTable("bind_jobs");
            entity.HasKey(x => x.BindJobId);

            entity.Property(x => x.BindJobId).HasColumnName("bind_job_id").HasMaxLength(64);
            entity.Property(x => x.QuoteId).HasColumnName("quote_id").HasMaxLength(64);
            entity.Property(x => x.SubmissionId).HasColumnName("submission_id").HasMaxLength(64);
            entity.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(50).HasConversion<string>();
            entity.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(200);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).HasConversion<string>();
            entity.Property(x => x.PolicyNumber).HasColumnName("policy_number").HasMaxLength(100);
            entity.Property(x => x.RetryCount).HasColumnName("retry_count");
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");

            entity.HasIndex(x => x.QuoteId).HasDatabaseName("IX_bind_quote");
            entity.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_bind_status");
        });
    }
}

