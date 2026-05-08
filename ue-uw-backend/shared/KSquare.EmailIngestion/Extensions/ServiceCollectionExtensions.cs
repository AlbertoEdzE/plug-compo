using KSquare.BlobStorage.Contracts;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Contracts;
using KSquare.EmailIngestion.HostedService;
using KSquare.EmailIngestion.Internal;
using KSquare.EmailIngestion.Providers.MicrosoftGraph;
using KSquare.EventBus.Contracts;
using KSquare.Idempotency.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Graph;

namespace KSquare.EmailIngestion.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsEmailIngestion(this IServiceCollection services, Action<EmailIngestionOptions> configure)
    {
        EnsureDependencyRegistered<IBlobStorageConnector>(services);
        EnsureDependencyRegistered<IEventPublisher>(services);
        EnsureDependencyRegistered<IIdempotencyGuard>(services);

        var options = new EmailIngestionOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<IEmailParser, MimeEmailParser>();
        services.TryAddSingleton<BlobAttachmentStore>();
        services.TryAddSingleton<IEmailAttachmentStore>(sp => sp.GetRequiredService<BlobAttachmentStore>());
        services.TryAddSingleton<IEmailDuplicateDetector, IdempotencyDuplicateDetector>();

        switch (options.Provider)
        {
            case EmailIngestionProvider.MicrosoftGraph:
                services.TryAddSingleton<GraphServiceClient>(_ => KSquare.EmailIngestion.Providers.MicrosoftGraph.GraphClientFactory.Create(options));
                services.TryAddSingleton<GraphEmailSource>(sp => new GraphEmailSource(options, sp.GetRequiredService<GraphServiceClient>()));
                services.TryAddSingleton<GraphEmailMover>(sp => new GraphEmailMover(options, sp.GetRequiredService<GraphServiceClient>()));
                services.TryAddSingleton<IEmailSource, GraphEmailSourceAdapter>();
                services.TryAddSingleton<IEmailMover, GraphEmailMoverAdapter>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported email ingestion provider.");
        }

        services.TryAddSingleton<IEmailIngestionConnector, EmailIngestionConnector>();
        services.AddHostedService<EmailIngestionHostedService>();

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsEmailIngestion.");
    }
}
