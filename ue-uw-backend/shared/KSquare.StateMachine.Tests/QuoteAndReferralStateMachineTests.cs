using FluentAssertions;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Definitions;
using KSquare.StateMachine.Tests.Synthesizers;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.StateMachine.Tests;

public sealed class QuoteAndReferralStateMachineTests
{
    [Fact]
    public async Task Quote_transitions_request_pricing_to_priced_to_proposal_generated()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot, o =>
        {
            o.PublishTransitionEvents = false;
            o.WriteAuditTrail = false;
        });
        using var scope = sp.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<QuoteState, QuoteTrigger>("Quote", "quote-1", QuoteState.Draft);

        var ctx = new StateMachineContextSynthesizer(seed: 7).Context();
        await machine.FireAsync(QuoteTrigger.RequestPricing, ctx);
        await machine.FireAsync(QuoteTrigger.PricingComplete, ctx);
        await machine.FireAsync(QuoteTrigger.GenerateProposal, ctx);

        machine.CurrentState.Should().Be(QuoteState.ProposalGenerated);
    }

    [Fact]
    public async Task Referral_request_info_round_trips_pending_info_to_under_review()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot, o =>
        {
            o.PublishTransitionEvents = false;
            o.WriteAuditTrail = false;
        });
        using var scope = sp.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<ReferralState, ReferralTrigger>("Referral", "ref-1", ReferralState.Open);

        var ctx = new StateMachineContextSynthesizer(seed: 8).Context();
        await machine.FireAsync(ReferralTrigger.BeginReview, ctx);
        await machine.FireAsync(ReferralTrigger.RequestInfo, ctx);
        await machine.FireAsync(ReferralTrigger.InfoReceived, ctx);

        machine.CurrentState.Should().Be(ReferralState.UnderReview);
    }
}

