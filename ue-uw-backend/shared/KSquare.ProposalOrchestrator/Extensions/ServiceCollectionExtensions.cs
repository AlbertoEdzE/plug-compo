using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Extensions;
using KSquare.EventBus.Contracts;
using KSquare.Idempotency.Contracts;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Contracts;
using KSquare.ProposalOrchestrator.Database;
using KSquare.ProposalOrchestrator.HostedService;
using KSquare.ProposalOrchestrator.Mapping;
using KSquare.ProposalOrchestrator.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.ProposalOrchestrator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsProposalOrchestrator(this IServiceCollection services, Action<ProposalOrchestratorOptions> configure)
    {
        EnsureDependencyRegistered<IBlobStorageConnector>(services);
        EnsureDependencyRegistered<IEventPublisher>(services);
        EnsureDependencyRegistered<IIdempotencyGuard>(services);
        EnsureDependencyRegistered<ICorrelationContextAccessor>(services);

        var options = new ProposalOrchestratorOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<IProposalPayloadBuilder, GhostDraftPayloadBuilder>();

        if (options.Provider == ProposalProvider.Mock)
        {
            services.TryAddSingleton<IProposalOrchestrator, MockProposalOrchestrator>();
            return services;
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for ProposalOrchestrator persistence.");
        }

        services.AddDbContext<ProposalDbContext>(db => db.UseSqlServer(options.ConnectionString));

        services.AddHttpClient("ghostdraft", client =>
        {
            if (!string.IsNullOrWhiteSpace(options.GhostDraftApiUrl))
            {
                client.BaseAddress = new Uri(options.GhostDraftApiUrl.TrimEnd('/') + "/");
            }
        }).AddKsCorrelationPropagation();

        services.TryAddScoped<GhostDraftProposalOrchestrator>();
        services.TryAddScoped<IProposalOrchestrator>(sp => sp.GetRequiredService<GhostDraftProposalOrchestrator>());
        services.AddHostedService<ProposalPollingHostedService>();

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsProposalOrchestrator.");
    }
}

