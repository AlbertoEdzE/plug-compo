using KSquare.BlobStorage.Contracts;
using KSquare.DocumentExtraction.Configuration;
using KSquare.DocumentExtraction.Contracts;
using KSquare.DocumentExtraction.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.DocumentExtraction.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsDocumentExtraction(this IServiceCollection services, Action<DocumentExtractionOptions> configure)
    {
        EnsureDependencyRegistered<IBlobStorageConnector>(services);

        var options = new DocumentExtractionOptions();
        configure(options);
        services.TryAddSingleton(options);

        switch (options.Provider)
        {
            case DocumentExtractionProvider.Mock:
                services.TryAddSingleton<IDocumentExtractor, MockDocumentExtractor>();
                break;
            default:
                services.AddHttpClient<FunctionHttpDocumentExtractor>();
                services.TryAddScoped<IDocumentExtractor>(sp => sp.GetRequiredService<FunctionHttpDocumentExtractor>());
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

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsDocumentExtraction.");
    }
}
