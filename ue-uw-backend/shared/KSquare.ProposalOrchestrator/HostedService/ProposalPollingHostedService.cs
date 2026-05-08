using System.Diagnostics;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Database;
using KSquare.ProposalOrchestrator.Models;
using KSquare.ProposalOrchestrator.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KSquare.ProposalOrchestrator.HostedService;

public sealed class ProposalPollingHostedService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("KSquare.ProposalOrchestrator");

    private readonly ProposalOrchestratorOptions _options;
    private readonly ProposalDbContext _db;
    private readonly GhostDraftProposalOrchestrator _orchestrator;
    private readonly ILogger<ProposalPollingHostedService> _logger;

    public ProposalPollingHostedService(
        ProposalOrchestratorOptions options,
        ProposalDbContext db,
        GhostDraftProposalOrchestrator orchestrator,
        ILogger<ProposalPollingHostedService> logger)
    {
        _options = options;
        _db = db;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollingInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Proposal polling tick failed.");
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("proposal.polling_tick", ActivityKind.Internal);
        var claimed = await ClaimPendingJobsAsync(maxJobs: 25, ct);

        foreach (var job in claimed)
        {
            try
            {
                await PollJobAsync(job, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling failed for proposal job {JobId}", job.JobId);
            }
        }
    }

    private async Task PollJobAsync(ProposalJobRecord job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.ProviderJobId))
        {
            return;
        }

        var (request, correlationId) = await _orchestrator.GetIdempotencyDataAsync(job, ct);
        var corr = correlationId ?? request?.CorrelationId ?? Guid.NewGuid().ToString();

        var status = await _orchestrator.GetProviderStatusAsync(job.ProviderJobId, corr, ct);
        var state = (status.Status ?? "").Trim();

        var now = DateTimeOffset.UtcNow;
        var pollAttempts = EstimatePollingAttempts(job.CreatedAt, now, _options.PollingInterval);
        if (pollAttempts > _options.MaxPollingAttempts)
        {
            await MarkFailedAsync(job.JobId, "TIMEOUT", corr, ct);
            return;
        }

        if (state.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(status.DownloadUrl))
            {
                await MarkFailedAsync(job.JobId, "Missing download URL in provider response.", corr, ct);
                return;
            }

            await _orchestrator.CompleteJobAsync(job.JobId, status.DownloadUrl, ct);
            return;
        }

        if (state.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            await HandleProviderFailureAsync(job.JobId, status.ErrorMessage ?? "Provider reported failure.", corr, ct);
            return;
        }

        await UpdateStatusAsync(
            job.JobId,
            state.Equals("processing", StringComparison.OrdinalIgnoreCase) ? ProposalJobStatus.Processing : ProposalJobStatus.Pending,
            null,
            ct
        );
    }

    private async Task HandleProviderFailureAsync(string jobId, string error, string correlationId, CancellationToken ct)
    {
        var job = await _db.ProposalJobs.FirstOrDefaultAsync(x => x.JobId == jobId, ct);
        if (job is null)
        {
            return;
        }

        job.RetryCount += 1;
        job.ErrorMessage = error;

        if (job.RetryCount <= _options.MaxRetryAttempts)
        {
            var newProviderJobId = await _orchestrator.ResubmitAsync(job, ct);
            if (!string.IsNullOrWhiteSpace(newProviderJobId))
            {
                job.ProviderJobId = newProviderJobId;
                job.Status = ProposalJobStatus.Pending;
                job.ErrorMessage = null;
                await _db.SaveChangesAsync(ct);
                return;
            }
        }

        job.Status = ProposalJobStatus.Failed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _orchestrator.PublishFailedAsync(job, error, correlationId, ct);
    }

    private async Task MarkFailedAsync(string jobId, string error, string correlationId, CancellationToken ct)
    {
        var job = await _db.ProposalJobs.FirstOrDefaultAsync(x => x.JobId == jobId, ct);
        if (job is null)
        {
            return;
        }

        job.Status = ProposalJobStatus.Failed;
        job.ErrorMessage = error;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _orchestrator.PublishFailedAsync(job, error, correlationId, ct);
    }

    private async Task UpdateStatusAsync(string jobId, ProposalJobStatus status, string? errorMessage, CancellationToken ct)
    {
        var job = await _db.ProposalJobs.FirstOrDefaultAsync(x => x.JobId == jobId, ct);
        if (job is null)
        {
            return;
        }

        job.Status = status;
        job.ErrorMessage = errorMessage;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<ProposalJobRecord>> ClaimPendingJobsAsync(int maxJobs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for ProposalPollingHostedService.");
        }

        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.Transaction = (SqlTransaction)tx;
        cmd.CommandText = """
            ;WITH claimed AS (
                SELECT TOP (@max)
                    job_id,
                    quote_id,
                    submission_id,
                    proposal_type,
                    provider,
                    provider_job_id,
                    status,
                    retry_count,
                    artifact_blob_path,
                    artifact_sas_url,
                    error_message,
                    created_at,
                    completed_at
                FROM proposal_generation_jobs WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE
                    status IN ('Pending', 'Processing')
                    AND completed_at IS NULL
                ORDER BY created_at ASC
            )
            UPDATE claimed
            SET status = 'Processing'
            OUTPUT
                inserted.job_id,
                inserted.quote_id,
                inserted.submission_id,
                inserted.proposal_type,
                inserted.provider,
                inserted.provider_job_id,
                inserted.status,
                inserted.retry_count,
                inserted.artifact_blob_path,
                inserted.artifact_sas_url,
                inserted.error_message,
                inserted.created_at,
                inserted.completed_at;
            """;
        cmd.Parameters.AddWithValue("@max", maxJobs);

        var jobs = new List<ProposalJobRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            jobs.Add(new ProposalJobRecord
            {
                JobId = reader.GetString(0),
                QuoteId = reader.GetString(1),
                SubmissionId = reader.GetString(2),
                ProposalType = reader.GetString(3),
                Provider = reader.GetString(4),
                ProviderJobId = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = Enum.TryParse<ProposalJobStatus>(reader.GetString(6), out var st) ? st : ProposalJobStatus.Pending,
                RetryCount = reader.GetInt32(7),
                ArtifactBlobPath = reader.IsDBNull(8) ? null : reader.GetString(8),
                ArtifactSasUrl = reader.IsDBNull(9) ? null : reader.GetString(9),
                ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
                CreatedAt = reader.GetDateTimeOffset(11),
                CompletedAt = reader.IsDBNull(12) ? null : reader.GetDateTimeOffset(12)
            });
        }

        await tx.CommitAsync(ct);
        return jobs;
    }

    private static int EstimatePollingAttempts(DateTimeOffset createdAt, DateTimeOffset now, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            return int.MaxValue;
        }

        var seconds = Math.Max(0, (now - createdAt).TotalSeconds);
        var attempt = (int)Math.Floor(seconds / interval.TotalSeconds) + 1;
        return Math.Max(1, attempt);
    }
}
