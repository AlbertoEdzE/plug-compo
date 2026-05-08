using FluentAssertions;
using KSquare.RulesEngine.Exceptions;
using KSquare.RulesEngine.Internal;

namespace KSquare.RulesEngine.Tests;

public sealed class YamlValidationTests
{
    [Fact]
    public void Invalid_yaml_throws_rule_configuration_exception_at_parse_time()
    {
        const string yaml = """
            version: "1.0"
            rules:
              - rule_name: MissingCondition
                priority: 1
                action: X
            """;

        var act = () => YamlRuleSetParser.Parse(yaml, "invalid.yml");
        act.Should().Throw<RuleConfigurationException>();
    }
}

