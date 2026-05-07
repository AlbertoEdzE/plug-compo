using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Middleware;
using KSquare.Idempotency.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace KSquare.Idempotency.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsIdempotency(this IServiceCollection services, Action<IdempotencyOptions> configure)
    {
        var options = new IdempotencyOptions();
        configure(options);

        services.TryAddSingleton(options);

        switch (options.Provider)
        {
            case IdempotencyProvider.SqlServer:
                services.AddScoped<IIdempotencyGuard, SqlServerIdempotencyGuard>();
                break;
            case IdempotencyProvider.Redis:
                services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options.RedisConnectionString!));
                services.AddScoped<IIdempotencyGuard, RedisIdempotencyGuard>();
                break;
            case IdempotencyProvider.InMemory:
                services.TryAddSingleton<IIdempotencyGuard, InMemoryIdempotencyGuard>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported idempotency provider.");
        }

        return services;
    }

    public static IApplicationBuilder UseKsIdempotency(this IApplicationBuilder app, string headerName = "Idempotency-Key")
    {
        app.UseMiddleware<IdempotencyMiddleware>(headerName);
        return app;
    }
}
