using System.Text;
using KSquare.ExtractionMapper.Configuration;
using KSquare.ExtractionMapper.Contracts;
using KSquare.ExtractionMapper.Models;
using Microsoft.Extensions.Caching.Memory;

namespace KSquare.ExtractionMapper.Rules;

public sealed class FileSystemRuleProvider(
    ExtractionMapperOptions options,
    IMemoryCache cache,
    EmbeddedYamlRuleProvider embeddedFallback) : IMappingRuleProvider
{
    public async Task<MappingRuleSet> GetRulesAsync(string documentType, CancellationToken ct = default)
    {
        var cacheKey = $"mapping-rules:{documentType.Trim()}";
        if (cache.TryGetValue(cacheKey, out MappingRuleSet? cached) && cached is not null)
        {
            return cached;
        }

        var directoryName = options.RulesBlobContainerName ?? "mapping-rules";
        var rulesDirectory = Path.Combine(AppContext.BaseDirectory, directoryName);

        foreach (var fileName in CandidateFileNames(documentType))
        {
            var filePath = Path.Combine(rulesDirectory, fileName);

            try
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                var yaml = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
                var ruleSet = EmbeddedYamlRuleProvider.DeserializeRuleSet(yaml);
                cache.Set(cacheKey, ruleSet, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = options.RuleCacheTtl
                });
                return ruleSet;
            }
            catch
            {
            }
        }

        var fallback = await embeddedFallback.GetRulesAsync(documentType, ct);
        cache.Set(cacheKey, fallback, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = options.RuleCacheTtl
        });
        return fallback;
    }

    private static IReadOnlyList<string> CandidateFileNames(string documentType)
    {
        var dt = documentType.Trim();
        var exact = $"{dt}.mapping.yml";
        var lower = $"{dt.ToLowerInvariant()}.mapping.yml";
        var upper = $"{dt.ToUpperInvariant()}.mapping.yml";

        if (string.Equals(exact, lower, StringComparison.Ordinal) && string.Equals(exact, upper, StringComparison.Ordinal))
        {
            return new[] { exact };
        }

        if (string.Equals(lower, upper, StringComparison.Ordinal))
        {
            return new[] { exact, lower };
        }

        return new[] { exact, lower, upper };
    }
}

