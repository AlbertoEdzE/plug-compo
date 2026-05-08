using KSquare.AuditTrail.Contracts;
using KSquare.EventBus.Contracts;
using KSquare.StateMachine.Configuration;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Core;
using KSquare.StateMachine.Database;
using KSquare.StateMachine.Mock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.StateMachine.Extensions;

public static class ServiceCollectionExtensions
{
    public static StateMachineRegistrationBuilder AddKsStateMachine(this IServiceCollection services, Action<StateMachineOptions> configure)
    {
        var options = new StateMachineOptions();
        configure(options);
        services.TryAddSingleton(options);

        if (options.Provider == StateMachineProvider.Mock)
        {
            services.TryAddSingleton<IStateMachineFactory, MockStateMachineFactory>();
            return new StateMachineRegistrationBuilder(services);
        }

        EnsureDependencyRegistered<IEventPublisher>(services);
        EnsureDependencyRegistered<IAuditTrailWriter>(services);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for StateMachine persistence.");
        }

        services.AddDbContext<StateMachineDbContext>(db => db.UseSqlServer(options.ConnectionString));
        services.TryAddScoped<IStateMachineFactory, StatelessStateMachineFactory>();

        return new StateMachineRegistrationBuilder(services);
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsStateMachine.");
    }
}

public sealed class StateMachineRegistrationBuilder
{
    private readonly IServiceCollection _services;

    internal StateMachineRegistrationBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public StateMachineRegistrationBuilder AddStateMachineDefinition<TState, TTrigger, TDefinition>()
        where TState : struct, Enum
        where TTrigger : struct, Enum
        where TDefinition : class, IStateMachineDefinition<TState, TTrigger>
    {
        _services.TryAddSingleton<IStateMachineDefinition<TState, TTrigger>, TDefinition>();
        return this;
    }
}

