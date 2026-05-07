using System.Collections.Concurrent;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Internal;
using KSquare.AuditTrail.Models;
using Microsoft.Extensions.Logging;

namespace KSquare.AuditTrail.Providers;

internal sealed class InMemoryAuditTrailWriter(
    AuditTrailOptions options,
    PiiMaskingSerializer masking,
    ConcurrentBag<AuditEntry> entries,
    ILogger<InMemoryAuditTrailWriter> logger
) : IAuditTrailWriter
{
    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var stored = entry with
            {
                ServiceName = entry.ServiceName ?? options.ServiceName,
                Before = masking.MaskJson(entry.Before),
                After = masking.MaskJson(entry.After)
            };

            entries.Add(stored);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit write dropped for {ResourceType}/{ResourceId}", entry.ResourceType, entry.ResourceId);
        }

        return Task.CompletedTask;
    }

    public async Task WriteChangeAsync<T>(
        string resourceType,
        string resourceId,
        string action,
        T? before,
        T? after,
        AuditActor actor,
        string? correlationId = null,
        CancellationToken ct = default
    )
        where T : class
    {
        try
        {
            var beforeJson = before is null ? null : System.Text.Json.JsonSerializer.Serialize(before);
            var afterJson = after is null ? null : System.Text.Json.JsonSerializer.Serialize(after);

            await WriteAsync(new AuditEntry
            {
                ResourceType = resourceType,
                ResourceId = resourceId,
                Action = action,
                Actor = actor,
                Before = beforeJson,
                After = afterJson,
                CorrelationId = correlationId
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit change write dropped for {ResourceType}/{ResourceId}", resourceType, resourceId);
        }
    }

    public async IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var filtered = entries.ToArray().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            filtered = filtered.Where(e => e.ResourceType.Equals(query.ResourceType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            filtered = filtered.Where(e => e.ResourceId.Equals(query.ResourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.ActorUserId))
        {
            filtered = filtered.Where(e => e.Actor.UserId.Equals(query.ActorUserId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            filtered = filtered.Where(e => e.Action.Equals(query.Action, StringComparison.OrdinalIgnoreCase));
        }

        if (query.From is not null)
        {
            filtered = filtered.Where(e => e.OccurredAt >= query.From.Value);
        }

        if (query.To is not null)
        {
            filtered = filtered.Where(e => e.OccurredAt <= query.To.Value);
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : query.PageSize;

        filtered = filtered
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        foreach (var entry in filtered)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }
}
