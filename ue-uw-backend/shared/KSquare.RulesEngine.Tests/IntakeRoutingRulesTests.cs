using FluentAssertions;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Extensions;
using KSquare.RulesEngine.Models;
using KSquare.RulesEngine.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.RulesEngine.Tests;

public sealed class IntakeRoutingRulesTests
{
    [Fact]
    public async Task HighValueAutoRoute_fires_when_tiv_exceeds_10m()
    {
        var rules = CreateEngine();
        var ctx = new RulesEngineDataSynthesizer(seed: 1).IntakeContext();
        ctx = new IntakeRoutingContext
        {
            TotalInsuredValue = 15_000_000m,
            BrokerTenureMonths = 24,
            NaicsCode = "5311",
            MissingRequiredFields = Array.Empty<string>(),
            NumberOfLocations = 3,
            SubmissionSource = "email"
        };

        var result = await rules.EvaluateAsync("intake-routing", ctx);
        result.FiredActions.First().Should().Be("RouteToSeniorUnderwriter");
        result.Results.Should().Contain(r => r.RuleName == "HighValueAutoRoute" && r.Fired);
    }

    [Fact]
    public async Task DefaultAutoAssign_fires_when_no_other_rule_matches()
    {
        var rules = CreateEngine();
        var ctx = new IntakeRoutingContext
        {
            TotalInsuredValue = 500_000m,
            BrokerTenureMonths = 36,
            NaicsCode = "5311",
            MissingRequiredFields = new[] { "foo", "bar" },
            NumberOfLocations = 1,
            SubmissionSource = "portal"
        };

        var action = await rules.GetFirstMatchedActionAsync("intake-routing", ctx);
        action.Should().Be("AutoAssign");

        var result = await rules.EvaluateAsync("intake-routing", ctx);
        result.Results.Should().Contain(r => r.RuleName == "DefaultAutoAssign" && r.Fired);
    }

    private static Contracts.IRulesEngine CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddKsRulesEngine(_ => { }).AddRuleSet("intake-routing").AddRuleSet("referral-triggers").AddRuleSet("bind-readiness");
        return services.BuildServiceProvider().GetRequiredService<Contracts.IRulesEngine>();
    }
}

