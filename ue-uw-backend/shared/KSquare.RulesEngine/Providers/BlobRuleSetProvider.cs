using System.Text;
using KSquare.BlobStorage.Contracts;
using KSquare.RulesEngine.Configuration;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Internal;
using KSquare.RulesEngine.Models;
using Microsoft.Extensions.Caching.Memory;

namespace KSquare.RulesEngine.Providers;

public sealed class BlobRuleSetProvider(
    RulesEngineOptions options,
    IBlobStorageConnector blobs,
    IMemoryCache cache
) : IRuleSetProvider
{
    public async Task<RuleSet> GetRuleSetAsync(string ruleSetName, CancellationToken ct = default)
    {
        var cacheKey = $"ruleset:{ruleSetName}";
        if (cache.TryGetValue(cacheKey, out RuleSet? cached) && cached is not null)
        {
            return cached;
        }

        var blobPath = $"{options.RulesBlobContainerName}/rules/{ruleSetName}.yml";
        var dl = await blobs.DownloadAsync(blobPath, ct);
        await using var stream = dl.Content;
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        var yaml = await reader.ReadToEndAsync(ct);

        var ruleSet = YamlRuleSetParser.Parse(yaml, sourceName: blobPath);
        cache.Set(cacheKey, ruleSet, options.CacheTtl);
        return ruleSet;
    }
}

