using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Extensions;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Contracts;
using KSquare.FormTemplates.FieldMaps;
using KSquare.FormTemplates.Internal;
using KSquare.FormTemplates.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.FormTemplates.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsFormTemplates(this IServiceCollection services, Action<FormTemplateOptions> configure)
    {
        EnsureDependencyRegistered<IBlobStorageConnector>(services);
        EnsureDependencyRegistered<ICorrelationContextAccessor>(services);

        var options = new FormTemplateOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<FieldMapLoader>();
        services.TryAddSingleton<IFormFieldMapper, ReflectionFieldMapper>();

        services.AddHttpClient("ghostdraft", client =>
        {
            if (!string.IsNullOrWhiteSpace(options.GhostDraftApiUrl))
            {
                client.BaseAddress = new Uri(options.GhostDraftApiUrl.TrimEnd('/') + "/");
            }
        }).AddKsCorrelationPropagation();

        services.TryAddScoped<GhostDraftFormEngine>();
        services.TryAddScoped<ITextPdfFormEngine>();
        services.TryAddScoped<LiquidFormEngine>();
        services.TryAddScoped<MockFormEngine>();

        services.TryAddScoped<IFormTemplateEngine>(sp =>
        {
            return options.Provider switch
            {
                FormTemplateProvider.GhostDraft => sp.GetRequiredService<GhostDraftFormEngine>(),
                FormTemplateProvider.ITextPdfFill => sp.GetRequiredService<ITextPdfFormEngine>(),
                FormTemplateProvider.Liquid => sp.GetRequiredService<LiquidFormEngine>(),
                FormTemplateProvider.Mock => sp.GetRequiredService<MockFormEngine>(),
                _ => sp.GetRequiredService<ITextPdfFormEngine>()
            };
        });

        return services;
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsFormTemplates.");
    }
}

