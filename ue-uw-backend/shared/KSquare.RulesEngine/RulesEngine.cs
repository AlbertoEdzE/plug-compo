using System.Linq.Expressions;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Exceptions;
using KSquare.RulesEngine.Models;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Parser;

namespace KSquare.RulesEngine;

public sealed class RulesEngine(IRuleSetProvider provider) : IRulesEngine
{
    private static readonly ParsingConfig ParsingConfig = new()
    {
        ResolveTypesBySimpleName = true
    };

    private readonly Dictionary<string, Dictionary<string, Delegate>> _compiled = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public async Task<RuleEvaluationResult> EvaluateAsync<TContext>(
        string ruleSetName,
        TContext context,
        CancellationToken ct = default
    )
        where TContext : class
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(ruleSetName))
        {
            throw new ArgumentException("ruleSetName is required.", nameof(ruleSetName));
        }

        var ruleSet = await provider.GetRuleSetAsync(ruleSetName, ct);
        var ordered = ruleSet.Rules.OrderByDescending(r => r.Priority).ToList();

        var results = new List<RuleResult>(ordered.Count);
        var firedActions = new List<string>();

        foreach (var rule in ordered)
        {
            ct.ThrowIfCancellationRequested();

            var predicate = GetOrCompilePredicate<TContext>(ruleSetName, rule);
            var fired = predicate(context);

            results.Add(new RuleResult
            {
                RuleName = rule.RuleName,
                Fired = fired,
                Action = fired ? rule.Action : null,
                Reason = rule.Reason
            });

            if (fired)
            {
                firedActions.Add(rule.Action);
            }
        }

        return new RuleEvaluationResult
        {
            RuleSetName = ruleSet.Name,
            Results = results,
            FiredActions = firedActions
        };
    }

    public async Task<string?> GetFirstMatchedActionAsync<TContext>(
        string ruleSetName,
        TContext context,
        CancellationToken ct = default
    )
        where TContext : class
    {
        var evaluated = await EvaluateAsync(ruleSetName, context, ct);
        return evaluated.FiredActions.FirstOrDefault();
    }

    private Func<TContext, bool> GetOrCompilePredicate<TContext>(string ruleSetName, Rule rule)
        where TContext : class
    {
        lock (_lock)
        {
            if (!_compiled.TryGetValue(ruleSetName, out var byRule))
            {
                byRule = new Dictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase);
                _compiled[ruleSetName] = byRule;
            }

            if (byRule.TryGetValue(rule.RuleName, out var existing))
            {
                if (existing is Func<TContext, bool> typed)
                {
                    return typed;
                }

                byRule.Remove(rule.RuleName);
            }

            var compiled = Compile<TContext>(ruleSetName, rule);
            byRule[rule.RuleName] = compiled;
            return compiled;
        }
    }

    private static Func<TContext, bool> Compile<TContext>(string ruleSetName, Rule rule)
        where TContext : class
    {
        try
        {
            var parameter = Expression.Parameter(typeof(TContext), "context");
            var lambda = DynamicExpressionParser.ParseLambda(
                ParsingConfig,
                new[] { parameter },
                typeof(bool),
                rule.Condition
            );

            var typed = (Expression<Func<TContext, bool>>)lambda;
            return typed.Compile();
        }
        catch (Exception ex)
        {
            throw new RuleConfigurationException($"Failed to compile condition for rule '{rule.RuleName}' in set '{ruleSetName}'.", ex);
        }
    }
}

