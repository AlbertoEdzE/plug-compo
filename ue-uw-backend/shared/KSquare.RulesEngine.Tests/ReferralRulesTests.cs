using FluentAssertions;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Extensions;
using KSquare.RulesEngine.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.RulesEngine.Tests;

public sealed class ReferralRulesTests
{
    [Fact]
    public async Task ReferralRequired_fires_when_largest_single_loss_exceeds_500k()
    {
        var rules = CreateEngine();
        var ctx = new RulesEngineDataSynthesizer(seed: 2).ReferralContext();
        ctx = new ReferralContext
        {
            LargestSingleLoss = 600_000m,
            FiveYearLossRatio = 0.1m,
            NumberOfLocations = 2,
            NaicsCode = "5311",
            OutOfAppetiteNaicsCodes = new[] { "9999" },
            TotalInsuredValue = 1_000_000m
        };

        var action = await rules.GetFirstMatchedActionAsync("referral-triggers", ctx);
        action.Should().Be("ReferralRequired");
    }

    [Fact]
    public async Task Decline_fires_when_naics_code_is_out_of_appetite()
    {
        var rules = CreateEngine();
        var ctx = new ReferralContext
        {
            LargestSingleLoss = 0m,
            FiveYearLossRatio = 0.1m,
            NumberOfLocations = 1,
            NaicsCode = "9999",
            OutOfAppetiteNaicsCodes = new[] { "9999" },
            TotalInsuredValue = 500_000m
        };

        var result = await rules.EvaluateAsync("referral-triggers", ctx);
        result.FiredActions.Should().Contain("Decline");
        result.Results.Should().Contain(r => r.RuleName == "OutOfAppetiteNaics" && r.Fired && r.Reason != null);
    }

    private static Contracts.IRulesEngine CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddKsRulesEngine(_ => { }).AddRuleSet("intake-routing").AddRuleSet("referral-triggers").AddRuleSet("bind-readiness");
        return services.BuildServiceProvider().GetRequiredService<Contracts.IRulesEngine>();
    }
}
