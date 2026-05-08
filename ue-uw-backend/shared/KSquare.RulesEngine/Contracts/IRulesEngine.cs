namespace KSquare.RulesEngine.Contracts;

using KSquare.RulesEngine.Models;

public interface IRulesEngine
{
    Task<RuleEvaluationResult> EvaluateAsync<TContext>(
        string ruleSetName,
        TContext context,
        CancellationToken ct = default
    )
        where TContext : class;

    Task<string?> GetFirstMatchedActionAsync<TContext>(
        string ruleSetName,
        TContext context,
        CancellationToken ct = default
    )
        where TContext : class;
}

