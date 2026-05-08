using KSquare.RiskAnalysis.Configuration;
using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Internal;
using KSquare.RulesEngine.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.RiskAnalysis.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsRiskAnalysis(this IServiceCollection services, Action<RiskAnalysisOptions> configure)
    {
        EnsureDependencyRegistered<IRulesEngine>(services);

        var options = new RiskAnalysisOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<LossRunAnalyzer>();
        services.TryAddSingleton<ILossRunAnalyzer>(sp => sp.GetRequiredService<LossRunAnalyzer>());

        services.TryAddSingleton<IRiskScorer, RiskScorerImpl>();
        services.TryAddScoped<IAppetiteCalculator, AppetiteCalculatorImpl>();

        services.TryAddScoped<IRiskAnalysisEngine, RiskAnalysisEngine>();

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsRiskAnalysis.");
    }
}

