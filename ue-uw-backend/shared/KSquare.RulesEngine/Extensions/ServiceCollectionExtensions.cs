using System.Reflection;
using KSquare.BlobStorage.Contracts;
using KSquare.RulesEngine.Configuration;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.RulesEngine.Extensions;

public static class ServiceCollectionExtensions
{
    public static RulesEngineBuilder AddKsRulesEngine(this IServiceCollection services, Action<RulesEngineOptions> configure)
    {
        var options = new RulesEngineOptions();
        configure(options);
        services.TryAddSingleton(options);

        services.AddMemoryCache();

        services.TryAddScoped<IRulesEngine, RulesEngine>();

        switch (options.RuleSource)
        {
            case RuleSetSource.EmbeddedYaml:
                services.TryAddSingleton<EmbeddedYamlRuleSetProvider>();
                services.TryAddSingleton<IRuleSetProvider>(sp => sp.GetRequiredService<EmbeddedYamlRuleSetProvider>());
                break;
            case RuleSetSource.BlobStorage:
                EnsureDependencyRegistered<IBlobStorageConnector>(services);
                services.TryAddSingleton<IRuleSetProvider, BlobRuleSetProvider>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.RuleSource), options.RuleSource, "Unsupported rule source.");
        }

        return new RulesEngineBuilder(services, options.RuleSource);
    }

    private static void EnsureDependencyRegistered<T>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(T).Name} must be registered before AddKsRulesEngine.");
    }
}

public sealed class RulesEngineBuilder
{
    private static readonly Assembly Assembly = typeof(RulesEngineBuilder).Assembly;
    private readonly IServiceCollection _services;
    private readonly RuleSetSource _source;

    internal RulesEngineBuilder(IServiceCollection services, RuleSetSource source)
    {
        _services = services;
        _source = source;
    }

    public RulesEngineBuilder AddRuleSet(string name)
    {
        if (_source == RuleSetSource.EmbeddedYaml && !HasEmbeddedRuleSet(name))
        {
            throw new InvalidOperationException($"Embedded rule set '{name}' not found.");
        }

        var list = GetOrAddRuleSetNames(_services);
        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(name);
        }

        return this;
    }

    private static List<string> GetOrAddRuleSetNames(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(List<string>));
        if (descriptor?.ImplementationInstance is List<string> existing)
        {
            return existing;
        }

        var list = new List<string>();
        services.AddSingleton(list);
        return list;
    }

    private static bool HasEmbeddedRuleSet(string name)
    {
        var expectedSuffix = $".Resources.rules.{name}.yml";
        return Assembly.GetManifestResourceNames().Any(r => r.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase));
    }
}
