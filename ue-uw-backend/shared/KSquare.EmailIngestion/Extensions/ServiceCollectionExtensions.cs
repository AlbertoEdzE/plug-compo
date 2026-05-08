using KSquare.BlobStorage.Contracts;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Contracts;
using KSquare.EmailIngestion.HostedService;
using KSquare.EmailIngestion.Internal;
using KSquare.EmailIngestion.Providers.HttpGraphStub;
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
            case EmailIngestionProvider.HttpGraphStub:
                if (string.IsNullOrWhiteSpace(options.GraphApiBaseUrl))
                {
                    throw new InvalidOperationException("HttpGraphStub provider requires GraphApiBaseUrl.");
                }

                services.AddHttpClient("ksquare-graph-stub", client =>
                {
                    client.BaseAddress = new Uri(options.GraphApiBaseUrl);
                });

                services.TryAddSingleton<HttpGraphEmailSource>(sp =>
                    new HttpGraphEmailSource(options, sp.GetRequiredService<IHttpClientFactory>().CreateClient("ksquare-graph-stub")));
                services.TryAddSingleton<HttpGraphEmailMover>(sp =>
                    new HttpGraphEmailMover(options, sp.GetRequiredService<IHttpClientFactory>().CreateClient("ksquare-graph-stub")));

                services.TryAddSingleton<IEmailSource>(sp => sp.GetRequiredService<HttpGraphEmailSource>());
                services.TryAddSingleton<IEmailMover>(sp => sp.GetRequiredService<HttpGraphEmailMover>());
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
