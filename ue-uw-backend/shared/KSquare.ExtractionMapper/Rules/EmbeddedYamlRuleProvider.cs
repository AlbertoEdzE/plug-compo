using System.Collections.Concurrent;
using System.Text;
using KSquare.ExtractionMapper.Contracts;
using KSquare.ExtractionMapper.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KSquare.ExtractionMapper.Rules;

public sealed class EmbeddedYamlRuleProvider : IMappingRuleProvider
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ConcurrentDictionary<string, MappingRuleSet> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<MappingRuleSet> GetRulesAsync(string documentType, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(documentType, out var cached))
        {
            return Task.FromResult(cached);
        }

        var rules = LoadFromEmbeddedYaml(documentType);
        _cache[documentType] = rules;
        return Task.FromResult(rules);
    }

    internal static MappingRuleSet LoadFromEmbeddedYaml(string documentType)
    {
        var assembly = typeof(EmbeddedYamlRuleProvider).Assembly;
        var normalized = documentType.Trim();
        var expectedSuffix = $"{normalized}.mapping.yml";

        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            var fallbackSuffix = $"{normalized.ToLowerInvariant()}.mapping.yml";
            resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fallbackSuffix, StringComparison.OrdinalIgnoreCase));
        }

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded mapping rules not found for document type '{documentType}'.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded mapping rules stream not found for resource '{resourceName}'.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        var yaml = reader.ReadToEnd();
        return DeserializeRuleSet(yaml);
    }

    internal static MappingRuleSet DeserializeRuleSet(string yaml)
    {
        var parsed = Deserializer.Deserialize<MappingRuleSetYaml>(yaml);

        if (parsed is null)
        {
            throw new InvalidOperationException("Failed to deserialize mapping rule set YAML.");
        }

        if (string.IsNullOrWhiteSpace(parsed.DocumentType))
        {
            throw new InvalidOperationException("Mapping rule set YAML missing 'document_type'.");
        }

        if (string.IsNullOrWhiteSpace(parsed.Version))
        {
            throw new InvalidOperationException("Mapping rule set YAML missing 'version'.");
        }

        var rules = parsed.Rules
            .Select(r => new FieldMappingRule
            {
                RuleId = r.RuleId ?? throw new InvalidOperationException("Rule missing 'rule_id'."),
                CanonicalField = r.CanonicalField ?? throw new InvalidOperationException("Rule missing 'canonical_field'."),
                SourceFieldNames = (r.SourceFields ?? Array.Empty<string>()).ToArray(),
                TargetType = r.TargetType ?? throw new InvalidOperationException("Rule missing 'target_type'."),
                DefaultValue = r.DefaultValue,
                Required = r.Required ?? false,
                TransformExpression = r.Transform
            })
            .ToArray();

        return new MappingRuleSet
        {
            DocumentType = parsed.DocumentType,
            Version = parsed.Version,
            Rules = rules
        };
    }

    private sealed class MappingRuleSetYaml
    {
        public string? DocumentType { get; set; }
        public string? Version { get; set; }
        public List<FieldMappingRuleYaml> Rules { get; set; } = new();
    }

    private sealed class FieldMappingRuleYaml
    {
        public string? RuleId { get; set; }
        public string? CanonicalField { get; set; }
        public string[]? SourceFields { get; set; }
        public string? TargetType { get; set; }
        public string? DefaultValue { get; set; }
        public bool? Required { get; set; }
        public string? Transform { get; set; }
    }
}

