using KSquare.AuditTrail.Contracts;
using KSquare.EventBus.Contracts;
using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Database;
using KSquare.PolicyAdminAdapter.HostedService;
using KSquare.PolicyAdminAdapter.Providers.Guidewire;
using KSquare.PolicyAdminAdapter.Providers.Mock;
using KSquare.PolicyAdminAdapter.Providers.Pcas;
using KSquare.PolicyAdminAdapter.Validation;
using KSquare.RulesEngine.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.PolicyAdminAdapter.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsPolicyAdminAdapter(this IServiceCollection services, Action<PolicyAdminAdapterOptions> configure)
    {
        EnsureDependencyRegistered<IEventPublisher>(services);
        EnsureDependencyRegistered<IAuditTrailWriter>(services);
        EnsureDependencyRegistered<IRulesEngine>(services);

        var options = new PolicyAdminAdapterOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<IBindReadinessValidator, RulesEngineBindValidator>();

        if (options.Provider == Models.PolicyAdminProvider.Mock)
        {
            services.TryAddSingleton<IPolicyAdminAdapter, MockPolicyAdminAdapter>();
            return services;
        }

        if (options.Provider == Models.PolicyAdminProvider.Guidewire)
        {
            services.TryAddSingleton<IPolicyAdminPayloadBuilder, GuidewirePayloadBuilder>();
            services.TryAddSingleton<IPolicyAdminAdapter, GuidewireBindAdapter>();
            return services;
        }

        if (string.IsNullOrWhiteSpace(options.SqlConnectionString))
        {
            throw new InvalidOperationException("SqlConnectionString is required for PolicyAdminAdapter persistence.");
        }

        services.AddDbContext<PolicyAdminDbContext>(db => db.UseSqlServer(options.SqlConnectionString));

        services.AddHttpClient("pcas", client =>
        {
            if (!string.IsNullOrWhiteSpace(options.PcasBaseUrl))
            {
                client.BaseAddress = new Uri(options.PcasBaseUrl.TrimEnd('/') + "/");
            }
        });

        services.TryAddSingleton<IPolicyAdminPayloadBuilder, PcasPayloadBuilder>();
        services.TryAddScoped<PcasBindAdapter>();
        services.TryAddScoped<IPolicyAdminAdapter>(sp => sp.GetRequiredService<PcasBindAdapter>());
        services.AddHostedService<BindPollingHostedService>();

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsPolicyAdminAdapter.");
    }
}

