using FluentAssertions;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.RulesEngine.Tests;

public sealed class BindReadinessRulesTests
{
    [Fact]
    public async Task BlockBind_fires_with_reason_when_quote_not_approved()
    {
        var rules = CreateEngine();
        var ctx = new BindReadinessContext
        {
            QuoteStatus = "Draft",
            HasSignedApplication = true,
            PremiumAgreedByBroker = true,
            ComplianceCheckPassed = true,
            ReferralApproved = true
        };

        var result = await rules.EvaluateAsync("bind-readiness", ctx);
        result.FiredActions.First().Should().Be("BlockBind");
        result.Results.Should().Contain(r => r.RuleName == "QuoteNotApproved" && r.Fired && r.Reason == "Quote must be in Approved status before binding");
    }

    [Fact]
    public async Task AllowBind_fires_when_all_bind_conditions_are_true()
    {
        var rules = CreateEngine();
        var ctx = new BindReadinessContext
        {
            QuoteStatus = "Approved",
            HasSignedApplication = true,
            PremiumAgreedByBroker = true,
            ComplianceCheckPassed = true,
            ReferralApproved = true
        };

        var action = await rules.GetFirstMatchedActionAsync("bind-readiness", ctx);
        action.Should().Be("AllowBind");
    }

    private static Contracts.IRulesEngine CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddKsRulesEngine(_ => { }).AddRuleSet("intake-routing").AddRuleSet("referral-triggers").AddRuleSet("bind-readiness");
        return services.BuildServiceProvider().GetRequiredService<Contracts.IRulesEngine>();
    }
}

