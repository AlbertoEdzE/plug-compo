namespace KSquare.RulesEngine.Contracts;

using KSquare.RulesEngine.Models;

public interface IRuleSetProvider
{
    Task<RuleSet> GetRuleSetAsync(string ruleSetName, CancellationToken ct = default);
}

