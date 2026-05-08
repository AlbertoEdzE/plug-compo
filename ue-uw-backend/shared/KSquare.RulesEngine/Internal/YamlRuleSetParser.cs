using KSquare.RulesEngine.Exceptions;
using KSquare.RulesEngine.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KSquare.RulesEngine.Internal;

internal static class YamlRuleSetParser
{
    public static RuleSet Parse(string yaml, string sourceName)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var doc = deserializer.Deserialize<RuleSetYaml>(yaml) ?? throw new RuleConfigurationException($"Rule set '{sourceName}' is empty.");
            var ruleSet = new RuleSet
            {
                Name = doc.Name ?? "",
                Version = doc.Version ?? "",
                Rules = (doc.Rules ?? new List<RuleYaml>()).Select(r => new Rule
                {
                    RuleName = r.RuleName ?? "",
                    Priority = r.Priority,
                    Description = r.Description,
                    Condition = r.Condition ?? "",
                    Action = r.Action ?? "",
                    Reason = r.Reason
                }).ToList()
            };

            Validate(ruleSet, sourceName);
            return ruleSet;
        }
        catch (RuleConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuleConfigurationException($"Failed to parse rule set '{sourceName}'.", ex);
        }
    }

    private static void Validate(RuleSet set, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(set.Name))
        {
            throw new RuleConfigurationException($"Rule set '{sourceName}' is missing 'name'.");
        }

        if (string.IsNullOrWhiteSpace(set.Version))
        {
            throw new RuleConfigurationException($"Rule set '{set.Name}' is missing 'version'.");
        }

        if (set.Rules.Count == 0)
        {
            throw new RuleConfigurationException($"Rule set '{set.Name}' must contain at least one rule.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in set.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleName))
            {
                throw new RuleConfigurationException($"Rule set '{set.Name}' contains a rule missing 'rule_name'.");
            }

            if (!names.Add(rule.RuleName))
            {
                throw new RuleConfigurationException($"Rule set '{set.Name}' contains duplicate rule '{rule.RuleName}'.");
            }

            if (string.IsNullOrWhiteSpace(rule.Condition))
            {
                throw new RuleConfigurationException($"Rule '{rule.RuleName}' in set '{set.Name}' is missing 'condition'.");
            }

            if (string.IsNullOrWhiteSpace(rule.Action))
            {
                throw new RuleConfigurationException($"Rule '{rule.RuleName}' in set '{set.Name}' is missing 'action'.");
            }
        }
    }

    private sealed class RuleSetYaml
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public List<RuleYaml>? Rules { get; set; }
    }

    private sealed class RuleYaml
    {
        public string? RuleName { get; set; }
        public int Priority { get; set; }
        public string? Description { get; set; }
        public string? Condition { get; set; }
        public string? Action { get; set; }
        public string? Reason { get; set; }
    }
}

