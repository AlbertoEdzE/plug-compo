using System.Reflection;
using System.Text;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Internal;
using KSquare.RulesEngine.Models;

namespace KSquare.RulesEngine.Providers;

public sealed class EmbeddedYamlRuleSetProvider : IRuleSetProvider
{
    private static readonly Assembly Assembly = typeof(EmbeddedYamlRuleSetProvider).Assembly;
    private readonly Dictionary<string, string> _resourceByRuleSet;
    private readonly Dictionary<string, RuleSet> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public EmbeddedYamlRuleSetProvider()
    {
        _resourceByRuleSet = FindEmbeddedRuleResources();
    }

    public Task<RuleSet> GetRuleSetAsync(string ruleSetName, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(ruleSetName, out var cached))
            {
                return Task.FromResult(cached);
            }
        }

        ct.ThrowIfCancellationRequested();

        if (!_resourceByRuleSet.TryGetValue(ruleSetName, out var resourceName))
        {
            throw new InvalidOperationException($"Embedded rule set '{ruleSetName}' not found.");
        }

        using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded rule set '{ruleSetName}' resource '{resourceName}' not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var yaml = reader.ReadToEnd();
        var ruleSet = YamlRuleSetParser.Parse(yaml, sourceName: ruleSetName);

        lock (_lock)
        {
            _cache[ruleSetName] = ruleSet;
        }

        return Task.FromResult(ruleSet);
    }

    internal bool HasRuleSet(string ruleSetName) => _resourceByRuleSet.ContainsKey(ruleSetName);

    private static Dictionary<string, string> FindEmbeddedRuleResources()
    {
        var names = Assembly.GetManifestResourceNames();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in names)
        {
            if (!resource.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!resource.Contains(".Resources.rules.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var marker = ".Resources.rules.";
            var idx = resource.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var start = idx + marker.Length;
            var namePart = resource.Substring(start);
            if (!namePart.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ruleSetName = namePart.Substring(0, namePart.Length - 4);
            if (string.IsNullOrWhiteSpace(ruleSetName))
            {
                continue;
            }

            result[ruleSetName] = resource;
        }

        return result;
    }
}
