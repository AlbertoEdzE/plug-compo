using Microsoft.EntityFrameworkCore;

namespace KSquare.ProposalOrchestrator.Database;

public sealed class ProposalDbContext(DbContextOptions<ProposalDbContext> options) : DbContext(options)
{
    public DbSet<ProposalJobRecord> ProposalJobs => Set<ProposalJobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProposalJobRecord>(entity =>
        {
            entity.ToTable("proposal_generation_jobs");
            entity.HasKey(x => x.JobId);

            entity.Property(x => x.JobId).HasColumnName("job_id").HasMaxLength(64);
            entity.Property(x => x.QuoteId).HasColumnName("quote_id").HasMaxLength(64);
            entity.Property(x => x.SubmissionId).HasColumnName("submission_id").HasMaxLength(64);
            entity.Property(x => x.ProposalType).HasColumnName("proposal_type").HasMaxLength(50);
            entity.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(50);
            entity.Property(x => x.ProviderJobId).HasColumnName("provider_job_id").HasMaxLength(200);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).HasConversion<string>();
            entity.Property(x => x.RetryCount).HasColumnName("retry_count");
            entity.Property(x => x.ArtifactBlobPath).HasColumnName("artifact_blob_path").HasMaxLength(1000);
            entity.Property(x => x.ArtifactSasUrl).HasColumnName("artifact_sas_url").HasMaxLength(2000);
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");

            entity.HasIndex(x => x.QuoteId).HasDatabaseName("IX_proposal_quote");
            entity.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_proposal_status");
        });
    }
}

