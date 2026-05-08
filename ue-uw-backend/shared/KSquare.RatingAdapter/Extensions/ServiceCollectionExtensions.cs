using KSquare.Correlation.Contracts;
using KSquare.Correlation.Extensions;
using KSquare.RatingAdapter.Configuration;
using KSquare.RatingAdapter.Contracts;
using KSquare.RatingAdapter.Mapping;
using KSquare.RatingAdapter.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.RatingAdapter.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsRatingAdapter(this IServiceCollection services, Action<RatingAdapterOptions> configure)
    {
        EnsureDependencyRegistered<ICorrelationContextAccessor>(services);

        var options = new RatingAdapterOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<ICoveragePricingMapper, UeRatingEnginePricingMapper>();

        switch (options.Provider)
        {
            case RatingProvider.Mock:
                services.TryAddSingleton<IRatingAdapter, MockRatingAdapter>();
                break;
            default:
                if (string.IsNullOrWhiteSpace(options.RatingEngineBaseUrl))
                {
                    throw new InvalidOperationException("RatingEngineBaseUrl must be set when using UeRatingEngine provider.");
                }

                services.AddHttpClient("rating-engine", client =>
                {
                    client.BaseAddress = new Uri(options.RatingEngineBaseUrl.TrimEnd('/') + "/");
                    client.Timeout = options.RequestTimeout;
                }).AddKsCorrelationPropagation();

                services.TryAddScoped<IRatingAdapter, UeRatingEngineAdapter>();
                break;
        }

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsRatingAdapter.");
    }
}

