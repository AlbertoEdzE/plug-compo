using KSquare.EmailSend.Contracts;
using KSquare.Notifications.Channels;
using KSquare.Notifications.Configuration;
using KSquare.Notifications.Contracts;
using KSquare.Notifications.Database;
using KSquare.Notifications.Internal;
using KSquare.PiiRedaction.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.Notifications.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsNotifications(this IServiceCollection services, Action<NotificationOptions> configure)
    {
        var options = new NotificationOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.AddKsPiiRedaction();

        if (options.EnableSms || options.EnableTeams)
        {
            throw new InvalidOperationException("SMS and Teams channels are not implemented.");
        }

        if (!options.EnableEmail && !options.EnableInApp)
        {
            throw new InvalidOperationException("At least one notification channel must be enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for Notifications persistence and deduplication.");
        }

        services.AddDbContext<NotificationDbContext>(db => db.UseSqlServer(options.ConnectionString));
        services.TryAddScoped<DedupService>();
        services.TryAddScoped<INotificationDispatcher, NotificationDispatcher>();

        if (options.EnableEmail)
        {
            EnsureDependencyRegistered<IEmailSender>(services);
            services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        }

        if (options.EnableInApp)
        {
            services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        }

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsNotifications.");
    }
}
