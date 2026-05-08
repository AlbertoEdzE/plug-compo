using KSquare.AgentOrchestrator.Configuration;
using KSquare.AgentOrchestrator.Contracts;
using KSquare.AgentOrchestrator.Providers;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.AgentOrchestrator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsAgentOrchestrator(this IServiceCollection services, Action<AgentOrchestratorOptions> configure)
    {
        EnsureDependencyRegistered<ICorrelationContextAccessor>(services);

        var options = new AgentOrchestratorOptions();
        configure(options);
        services.TryAddSingleton(options);

        services
            .AddHttpClient<FunctionHttpAgentOrchestratorClient>()
            .AddKsCorrelationPropagation();

        services.TryAddScoped<IAgentOrchestratorClient>(sp => sp.GetRequiredService<FunctionHttpAgentOrchestratorClient>());

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsAgentOrchestrator.");
    }
}

