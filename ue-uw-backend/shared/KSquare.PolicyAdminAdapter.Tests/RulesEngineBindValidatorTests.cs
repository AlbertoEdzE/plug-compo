using FluentAssertions;
using KSquare.PolicyAdminAdapter.Validation;
using KSquare.PolicyAdminAdapter.Tests.Synthesizers;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Models;
using Moq;

namespace KSquare.PolicyAdminAdapter.Tests;

public sealed class RulesEngineBindValidatorTests
{
    [Fact]
    public async Task Returns_not_ready_when_rules_engine_fires_block_bind_rule()
    {
        var rules = new Mock<IRulesEngine>();
        rules.Setup(r => r.EvaluateAsync("bind-readiness", It.IsAny<BindReadinessContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuleEvaluationResult
            {
                RuleSetName = "bind-readiness",
                Results = new[]
                {
                    new RuleResult { RuleName = "QuoteNotApproved", Fired = true, Action = "BlockBind", Reason = "Quote must be Approved" }
                },
                FiredActions = new[] { "BlockBind" }
            });

        var validator = new RulesEngineBindValidator(rules.Object);
        var req = new PolicyAdminDataSynthesizer(seed: 3).BindRequest();

        var result = await validator.ValidateAsync(req);

        result.IsReady.Should().BeFalse();
        result.Issues.Should().ContainSingle(i => i.Code == "QuoteNotApproved");
    }
}
