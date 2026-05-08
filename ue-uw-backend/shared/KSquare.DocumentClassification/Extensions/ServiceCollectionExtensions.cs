using KSquare.BlobStorage.Contracts;
using KSquare.DocumentClassification.Configuration;
using KSquare.DocumentClassification.Contracts;
using KSquare.DocumentClassification.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.DocumentClassification.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsDocumentClassification(this IServiceCollection services, Action<DocumentClassificationOptions> configure)
    {
        EnsureDependencyRegistered<IBlobStorageConnector>(services);

        var options = new DocumentClassificationOptions();
        configure(options);
        services.TryAddSingleton(options);

        if (options.Provider == DocumentClassificationProvider.Mock)
        {
            services.TryAddSingleton<IDocumentClassifier, MockDocumentClassifier>();
            return services;
        }

        services.AddHttpClient<FunctionHttpDocumentClassifier>();
        services.TryAddScoped<IDocumentClassifier>(sp => sp.GetRequiredService<FunctionHttpDocumentClassifier>());

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsDocumentClassification.");
    }
}
