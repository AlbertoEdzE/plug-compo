using FluentAssertions;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Extensions;
using KSquare.Idempotency.Models;
using KSquare.Idempotency.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.Idempotency.Tests;

public sealed class IdempotencyGuardTests
{
    [Fact]
    public async Task Get_then_set_then_get_returns_cached_result()
    {
        var synthesizer = new IdempotencyDataSynthesizer();
        var key = synthesizer.Key();
        var result = synthesizer.Result(statusCode: 201);

        var guard = CreateGuard();

        (await guard.GetAsync(key)).Should().BeNull();

        await guard.SetAsync(key, result);

        var cached = await guard.GetAsync(key);
        cached.Should().NotBeNull();
        cached!.StatusCode.Should().Be(201);
        cached.ResponseBody.Should().Be(result.ResponseBody);
        cached.ContentType.Should().Be(result.ContentType);
    }

    [Fact]
    public async Task TryMarkProcessed_is_atomic_for_repeated_calls()
    {
        var synthesizer = new IdempotencyDataSynthesizer();
        var messageId = synthesizer.MessageId();

        var guard = CreateGuard();

        (await guard.TryMarkProcessedAsync(messageId)).Should().BeTrue();
        (await guard.TryMarkProcessedAsync(messageId)).Should().BeFalse();
    }

    [Fact]
    public async Task Expired_http_keys_return_null()
    {
        var synthesizer = new IdempotencyDataSynthesizer();
        var key = synthesizer.Key();
        var result = synthesizer.Result();

        var guard = CreateGuard(options => options.DefaultHttpKeyTtl = TimeSpan.FromMilliseconds(100));

        await guard.SetAsync(key, result);
        (await guard.GetAsync(key)).Should().NotBeNull();

        await Task.Delay(200);

        (await guard.GetAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task TryMarkProcessed_is_concurrent_safe()
    {
        var synthesizer = new IdempotencyDataSynthesizer();
        var messageId = synthesizer.MessageId();
        var guard = CreateGuard();

        var tasks = Enumerable.Range(0, 100).Select(_ => guard.TryMarkProcessedAsync(messageId));
        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
    }

    private static IIdempotencyGuard CreateGuard(Action<IdempotencyOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddKsIdempotency(options =>
        {
            options.Provider = IdempotencyProvider.InMemory;
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider().GetRequiredService<IIdempotencyGuard>();
    }
}
