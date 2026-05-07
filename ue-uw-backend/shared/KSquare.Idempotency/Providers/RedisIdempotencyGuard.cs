using System.Text.Json;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Models;
using StackExchange.Redis;

namespace KSquare.Idempotency.Providers;

public sealed class RedisIdempotencyGuard(IdempotencyOptions options, IConnectionMultiplexer redis) : IIdempotencyGuard
{
    private const string HttpPrefix = "idempotency:";
    private const string ConsumerPrefix = "consumer:";

    public async Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var db = redis.GetDatabase();
        var value = await db.StringGetAsync($"{HttpPrefix}{key}").ConfigureAwait(false);
        if (!value.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<IdempotencyResult>(value!)!;
    }

    public async Task SetAsync(string key, IdempotencyResult result, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var effectiveTtl = ttl ?? options.DefaultHttpKeyTtl;
        var db = redis.GetDatabase();
        var redisKey = $"{HttpPrefix}{key}";

        if (result.StatusCode < 0)
        {
            await db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return;
        }

        var json = JsonSerializer.Serialize(result);
        if (result.StatusCode == 0)
        {
            await db.StringSetAsync(redisKey, json, effectiveTtl, When.NotExists).ConfigureAwait(false);
            return;
        }

        await db.StringSetAsync(redisKey, json, effectiveTtl).ConfigureAwait(false);
    }

    public async Task<bool> TryMarkProcessedAsync(string messageId, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var effectiveTtl = ttl ?? options.DefaultConsumerKeyTtl;
        var db = redis.GetDatabase();
        return await db.StringSetAsync($"{ConsumerPrefix}{messageId}", "1", effectiveTtl, When.NotExists).ConfigureAwait(false);
    }
}
