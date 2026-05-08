using KSquare.BlobStorage.Contracts;
using KSquare.ExtractionMapper.Configuration;
using KSquare.ExtractionMapper.Contracts;
using KSquare.ExtractionMapper.Internal;
using KSquare.ExtractionMapper.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.ExtractionMapper.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsExtractionMapper(this IServiceCollection services, Action<ExtractionMapperOptions> configure)
    {
        var options = new ExtractionMapperOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.AddMemoryCache();
        services.TryAddSingleton<EmbeddedYamlRuleProvider>();

        services.TryAddSingleton<IMappingRuleProvider>(sp =>
        {
            var embedded = sp.GetRequiredService<EmbeddedYamlRuleProvider>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();

            return options.RuleSource switch
            {
                MappingRuleSource.EmbeddedYaml => embedded,
                MappingRuleSource.BlobStorage => CreateBlobProvider(sp, options, cache, embedded),
                MappingRuleSource.FileSystem => new FileSystemRuleProvider(options, cache, embedded),
                _ => embedded
            };
        });

        services.TryAddSingleton<IExtractionMapper, FieldMapper>();

        return services;
    }

    private static IMappingRuleProvider CreateBlobProvider(
        IServiceProvider sp,
        ExtractionMapperOptions options,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        EmbeddedYamlRuleProvider embedded)
    {
        EnsureDependencyRegistered<IBlobStorageConnector>(sp);
        var blob = sp.GetRequiredService<IBlobStorageConnector>();
        return new BlobRuleProvider(options, blob, cache, embedded);
    }

    private static void EnsureDependencyRegistered<T>(IServiceProvider provider) where T : notnull
    {
        try
        {
            provider.GetRequiredService<T>();
        }
        catch
        {
            throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsExtractionMapper when using {nameof(MappingRuleSource.BlobStorage)} rule source.");
        }
    }
}
