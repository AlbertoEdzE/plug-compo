namespace KSquare.StateMachine.Definitions;

using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Core;

public enum QuoteState
{
    Draft,
    PricingRequested,
    Priced,
    ProposalGenerated,
    Presented,
    Accepted,
    Expired,
    Voided
}

public enum QuoteTrigger
{
    RequestPricing,
    PricingComplete,
    GenerateProposal,
    ProposalReady,
    Present,
    Accept,
    Expire,
    Void
}

public sealed class QuoteStateMachineDefinition : IStateMachineDefinition<QuoteState, QuoteTrigger>
{
    public void Configure(StateMachineBuilder<QuoteState, QuoteTrigger> builder)
    {
        builder.State(QuoteState.Draft)
            .Permit(QuoteTrigger.RequestPricing, QuoteState.PricingRequested)
            .Permit(QuoteTrigger.Void, QuoteState.Voided);

        builder.State(QuoteState.PricingRequested)
            .Permit(QuoteTrigger.PricingComplete, QuoteState.Priced)
            .Permit(QuoteTrigger.Void, QuoteState.Voided);

        builder.State(QuoteState.Priced)
            .Permit(QuoteTrigger.GenerateProposal, QuoteState.ProposalGenerated)
            .Permit(QuoteTrigger.Void, QuoteState.Voided);

        builder.State(QuoteState.ProposalGenerated)
            .Permit(QuoteTrigger.ProposalReady, QuoteState.Presented)
            .Permit(QuoteTrigger.Void, QuoteState.Voided);

        builder.State(QuoteState.Presented)
            .Permit(QuoteTrigger.Accept, QuoteState.Accepted)
            .Permit(QuoteTrigger.Expire, QuoteState.Expired)
            .Permit(QuoteTrigger.Void, QuoteState.Voided);

        builder.State(QuoteState.Accepted).IsTerminal();
        builder.State(QuoteState.Expired).IsTerminal();
        builder.State(QuoteState.Voided).IsTerminal();
    }
}

