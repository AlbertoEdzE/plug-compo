using System.Collections.Concurrent;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Models;

namespace KSquare.Idempotency.Providers;

public sealed class InMemoryIdempotencyGuard(IdempotencyOptions options) : IIdempotencyGuard
{
    private readonly ConcurrentDictionary<string, StoredHttpResult> _http = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumer = new(StringComparer.Ordinal);

    public Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default)
    {
        if (!_http.TryGetValue(key, out var stored))
        {
            return Task.FromResult<IdempotencyResult?>(null);
        }

        if (stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _http.TryRemove(key, out _);
            return Task.FromResult<IdempotencyResult?>(null);
        }

        return Task.FromResult<IdempotencyResult?>(stored.Result);
    }

    public Task SetAsync(string key, IdempotencyResult result, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (result.StatusCode < 0)
        {
            _http.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        var effectiveTtl = ttl ?? options.DefaultHttpKeyTtl;
        var expiresAt = DateTimeOffset.UtcNow.Add(effectiveTtl);
        if (result.StatusCode == 0)
        {
            _http.TryAdd(key, new StoredHttpResult(result, expiresAt));
            return Task.CompletedTask;
        }

        _http[key] = new StoredHttpResult(result, expiresAt);
        return Task.CompletedTask;
    }

    public Task<bool> TryMarkProcessedAsync(string messageId, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var effectiveTtl = ttl ?? options.DefaultConsumerKeyTtl;
        var expiresAt = DateTimeOffset.UtcNow.Add(effectiveTtl);

        while (true)
        {
            var existingExpiresAt = _consumer.GetOrAdd(messageId, expiresAt);
            if (existingExpiresAt == expiresAt)
            {
                return Task.FromResult(true);
            }

            if (existingExpiresAt > DateTimeOffset.UtcNow)
            {
                return Task.FromResult(false);
            }

            _consumer.TryUpdate(messageId, expiresAt, existingExpiresAt);
        }
    }

    private readonly record struct StoredHttpResult(IdempotencyResult Result, DateTimeOffset ExpiresAt);
}
