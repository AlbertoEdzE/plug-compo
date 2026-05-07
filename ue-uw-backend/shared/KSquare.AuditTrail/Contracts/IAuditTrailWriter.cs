using KSquare.AuditTrail.Models;

namespace KSquare.AuditTrail.Contracts;

public interface IAuditTrailWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);

    Task WriteChangeAsync<T>(
        string resourceType,
        string resourceId,
        string action,
        T? before,
        T? after,
        AuditActor actor,
        string? correlationId = null,
        CancellationToken ct = default
    )
        where T : class;

    IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query, CancellationToken ct = default);
}
