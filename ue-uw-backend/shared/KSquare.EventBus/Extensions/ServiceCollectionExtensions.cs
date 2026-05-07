using Azure.Messaging.ServiceBus;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Consumers;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Outbox;
using KSquare.EventBus.Publishers;
using KSquare.EventBus.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.EventBus.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsEventBus(this IServiceCollection services, Action<EventBusOptions> configure)
    {
        var options = new EventBusOptions();
        configure(options);

        services.TryAddSingleton(options);

        var registrations = GetOrAddRegistrations(services);
        services.TryAddSingleton<IReadOnlyCollection<ConsumerRegistration>>(_ => registrations);

        switch (options.Provider)
        {
            case EventBusProvider.InMemory:
                services.TryAddSingleton<InMemoryEventBusProvider>();
                services.TryAddSingleton<IDirectEventPublisher>(sp => sp.GetRequiredService<InMemoryEventBusProvider>());
                break;
            case EventBusProvider.AzureServiceBus:
                services.TryAddSingleton<AzureServiceBusProvider>();
                services.TryAddSingleton<ServiceBusClient>(sp => sp.GetRequiredService<AzureServiceBusProvider>().Client);
                services.TryAddSingleton<DirectServiceBusPublisher>();
                services.TryAddSingleton<IDirectEventPublisher>(sp => sp.GetRequiredService<DirectServiceBusPublisher>());
                services.AddHostedService<ServiceBusConsumerHost>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported event bus provider.");
        }

        if (options.UseOutbox)
        {
            if (string.IsNullOrWhiteSpace(options.OutboxConnectionString))
            {
                throw new InvalidOperationException("OutboxConnectionString must be provided when UseOutbox is enabled.");
            }

            services.AddDbContext<OutboxDbContext>(db => db.UseSqlServer(options.OutboxConnectionString));
            services.AddHostedService<OutboxRelay>();
            services.TryAddSingleton<IOutboxRelay>(sp => sp.GetRequiredService<OutboxRelay>());
            services.TryAddSingleton<IEventPublisher, OutboxEventPublisher>();
        }
        else
        {
            services.TryAddSingleton<IEventPublisher, DirectEventPublisher>();
        }

        return services;
    }

    public static IServiceCollection AddConsumer<TMessage, TConsumer>(this IServiceCollection services, string topic, string subscription)
        where TMessage : class
        where TConsumer : class, IEventConsumer<TMessage>
    {
        var registrations = GetOrAddRegistrations(services);
        registrations.Add(new ConsumerRegistration(typeof(TMessage), typeof(TConsumer), topic, subscription));

        services.AddScoped<TConsumer>();
        return services;
    }

    private static List<ConsumerRegistration> GetOrAddRegistrations(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(List<ConsumerRegistration>));
        if (descriptor?.ImplementationInstance is List<ConsumerRegistration> existing)
        {
            return existing;
        }

        var list = new List<ConsumerRegistration>();
        services.AddSingleton(list);
        return list;
    }
}
