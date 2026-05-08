using System.Text.Json;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Models;
using KSquare.EventBus.Contracts;
using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Database;
using KSquare.PolicyAdminAdapter.Models;
using KSquare.PolicyAdminAdapter.Providers.Pcas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KSquare.PolicyAdminAdapter.HostedService;

public sealed class BindPollingHostedService(
    IServiceScopeFactory scopeFactory,
    PolicyAdminAdapterOptions options,
    ILogger<BindPollingHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollingInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollOnceAsync(stoppingToken);
        }
    }

    internal async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PolicyAdminDbContext>();
        var adapter = scope.ServiceProvider.GetRequiredService<PcasBindAdapter>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

        var jobs = await db.BindJobs
            .Where(x => x.Status == BindJobStatus.Pending || x.Status == BindJobStatus.Submitted || x.Status == BindJobStatus.Processing)
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            try
            {
                await ProcessJobAsync(adapter, db, publisher, audit, job, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BindPollingHostedService failed for bind job {BindJobId}", job.BindJobId);
            }
        }
    }

    private async Task ProcessJobAsync(
        PcasBindAdapter adapter,
        PolicyAdminDbContext db,
        IEventPublisher publisher,
        IAuditTrailWriter audit,
        BindJobRecord job,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;
        var pollAttempt = EstimatePollAttempt(job.CreatedAt, now, options.PollingInterval);
        if (pollAttempt > options.MaxPollingAttempts)
        {
            await MarkFailedAsync(db, publisher, audit, job, "TIMEOUT", "Polling exceeded maximum attempts.", now, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(job.ProviderTransactionId))
        {
            return;
        }

        var (status, policyNumber, errorCode, errorMessage) = await adapter.GetProviderStatusAsync(job.ProviderTransactionId, correlationId: null, ct);
        var normalized = status.Trim().ToLowerInvariant();

        if (normalized == "issued")
        {
            job.Status = BindJobStatus.Bound;
            job.PolicyNumber = policyNumber;
            job.CompletedAt = now;
            await db.SaveChangesAsync(ct);

            await TryAuditAsync(audit, job.QuoteId, "PolicyBound", now, ct);

            var snapshot = ParseSnapshot(job.PayloadJson);
            if (snapshot is not null && !string.IsNullOrWhiteSpace(job.PolicyNumber))
            {
                var evt = new PolicyBoundEvent
                {
                    QuoteId = job.QuoteId,
                    SubmissionId = job.SubmissionId,
                    BindJobId = job.BindJobId,
                    PolicyNumber = job.PolicyNumber,
                    EffectiveDate = snapshot.EffectiveDate,
                    ExpirationDate = snapshot.ExpirationDate,
                    TotalAnnualPremium = snapshot.TotalAnnualPremium,
                    BoundAt = now
                };

                await publisher.PublishAsync(options.BoundEventTopic, nameof(PolicyBoundEvent), evt, ct: ct);
            }

            return;
        }

        if (normalized == "failed")
        {
            job.RetryCount += 1;
            if (job.RetryCount < options.MaxRetryAttempts && !string.IsNullOrWhiteSpace(job.PayloadJson))
            {
                var (newTxn, newStatus) = await adapter.ResubmitAsync(job.PayloadJson, correlationId: null, ct);
                job.ProviderTransactionId = newTxn;
                job.Status = newStatus;
                await db.SaveChangesAsync(ct);

                await TryAuditAsync(audit, job.QuoteId, "BindResubmitted", now, ct);
                return;
            }

            await MarkFailedAsync(db, publisher, audit, job, errorCode ?? "PAM_FAILED", errorMessage ?? "PCAS reported bind failure.", now, ct);
            return;
        }

        if (job.Status != BindJobStatus.Processing)
        {
            job.Status = BindJobStatus.Processing;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task MarkFailedAsync(
        PolicyAdminDbContext db,
        IEventPublisher publisher,
        IAuditTrailWriter audit,
        BindJobRecord job,
        string errorCode,
        string errorMessage,
        DateTimeOffset failedAt,
        CancellationToken ct
    )
    {
        if (job.Status == BindJobStatus.Failed)
        {
            return;
        }

        job.Status = BindJobStatus.Failed;
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;
        job.CompletedAt = failedAt;
        await db.SaveChangesAsync(ct);

        await TryAuditAsync(audit, job.QuoteId, "BindFailed", failedAt, ct);

        var evt = new BindFailedEvent
        {
            QuoteId = job.QuoteId,
            SubmissionId = job.SubmissionId,
            BindJobId = job.BindJobId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            FailedAt = failedAt
        };

        await publisher.PublishAsync(options.FailedEventTopic, nameof(BindFailedEvent), evt, ct: ct);
    }

    private static int EstimatePollAttempt(DateTimeOffset createdAt, DateTimeOffset now, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            return int.MaxValue;
        }

        var elapsed = now - createdAt;
        return (int)Math.Floor(elapsed.TotalMilliseconds / interval.TotalMilliseconds);
    }

    private static async Task TryAuditAsync(IAuditTrailWriter audit, string quoteId, string action, DateTimeOffset occurredAt, CancellationToken ct)
    {
        try
        {
            await audit.WriteAsync(new AuditEntry
            {
                ResourceType = "Quote",
                ResourceId = quoteId,
                Action = action,
                Actor = new AuditActor("policy-admin-adapter", "Policy Admin Adapter", ActorType: AuditActorType.ServiceAccount),
                ServiceName = "KSquare.PolicyAdminAdapter",
                OccurredAt = occurredAt
            }, ct);
        }
        catch
        {
        }
    }

    private static BindRequestSnapshot? ParseSnapshot(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("effectiveDate", out var eff) || !root.TryGetProperty("expirationDate", out var exp))
        {
            return null;
        }

        if (!DateOnly.TryParse(eff.GetString(), out var effectiveDate))
        {
            return null;
        }

        if (!DateOnly.TryParse(exp.GetString(), out var expirationDate))
        {
            return null;
        }

        decimal premium = 0m;
        if (root.TryGetProperty("totalAnnualPremium", out var prem))
        {
            if (prem.ValueKind == JsonValueKind.Number && prem.TryGetDecimal(out var d))
            {
                premium = d;
            }
            else if (decimal.TryParse(prem.ToString(), out var parsed))
            {
                premium = parsed;
            }
        }

        return new BindRequestSnapshot(effectiveDate, expirationDate, premium);
    }

    private sealed record BindRequestSnapshot(DateOnly EffectiveDate, DateOnly ExpirationDate, decimal TotalAnnualPremium);
}

