using System.Collections.Concurrent;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Internal;
using KSquare.AuditTrail.Models;
using KSquare.AuditTrail.Providers;
using KSquare.PiiRedaction.Configuration;
using KSquare.PiiRedaction.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.AuditTrail.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsAuditTrail(this IServiceCollection services, Action<AuditTrailOptions> configure)
    {
        var options = new AuditTrailOptions();
        configure(options);

        services.TryAddSingleton(options);

        if (options.MaskPiiInBeforeAfter)
        {
            services.AddKsPiiRedaction(pii =>
            {
                pii.PiiFieldNames = options.PiiFieldNames.ToList();
                pii.RedactionToken = "[REDACTED]";
            });
        }
        else
        {
            services.AddKsPiiRedaction();
        }

        services.TryAddScoped<PiiMaskingSerializer>();

        switch (options.Provider)
        {
            case AuditProvider.SqlServer:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new InvalidOperationException("SqlServer provider requires ConnectionString.");
                }

                services.AddScoped<IAuditTrailWriter, SqlServerAuditTrailWriter>();
                break;
            case AuditProvider.InMemory:
                services.TryAddSingleton<ConcurrentBag<AuditEntry>>();
                services.AddSingleton<IAuditTrailWriter, InMemoryAuditTrailWriter>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported audit provider.");
        }

        return services;
    }
}
