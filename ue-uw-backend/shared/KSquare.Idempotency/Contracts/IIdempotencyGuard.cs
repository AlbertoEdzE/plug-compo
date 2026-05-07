namespace KSquare.Idempotency.Contracts;

using KSquare.Idempotency.Models;

public interface IIdempotencyGuard
{
    Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, IdempotencyResult result, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<bool> TryMarkProcessedAsync(string messageId, TimeSpan? ttl = null, CancellationToken ct = default);
}
