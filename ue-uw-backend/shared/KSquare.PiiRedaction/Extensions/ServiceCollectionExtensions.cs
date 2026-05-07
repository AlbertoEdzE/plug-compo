using KSquare.PiiRedaction.Configuration;
using KSquare.PiiRedaction.Contracts;
using KSquare.PiiRedaction.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.PiiRedaction.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsPiiRedaction(
        this IServiceCollection services,
        Action<PiiRedactionOptions>? configure = null
    )
    {
        var options = new PiiRedactionOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IPiiRedactor, JsonPiiRedactor>();

        return services;
    }
}
