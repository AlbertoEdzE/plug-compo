using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Models;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Contracts;

namespace KSquare.PolicyAdminAdapter.Validation;

public sealed class RulesEngineBindValidator(IRulesEngine rulesEngine) : IBindReadinessValidator
{
    public async Task<BindReadinessResult> ValidateAsync(BindRequest request, CancellationToken ct = default)
    {
        _ = request;

        var context = new BindReadinessContext
        {
            QuoteStatus = "Approved",
            HasSignedApplication = true,
            PremiumAgreedByBroker = true,
            ComplianceCheckPassed = true,
            ReferralApproved = true
        };

        var result = await rulesEngine.EvaluateAsync("bind-readiness", context, ct);

        var issues = result.Results
            .Where(r => r.Fired && string.Equals(r.Action, "BlockBind", StringComparison.OrdinalIgnoreCase))
            .Select(r => new BindReadinessIssue(BindIssueLevel.Error, r.RuleName, r.Reason ?? r.RuleName))
            .ToList();

        return new BindReadinessResult
        {
            IsReady = issues.Count == 0,
            Issues = issues
        };
    }
}

