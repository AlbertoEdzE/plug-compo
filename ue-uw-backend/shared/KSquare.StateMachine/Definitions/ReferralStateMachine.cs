namespace KSquare.StateMachine.Definitions;

using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Core;

public enum ReferralState
{
    Open,
    UnderReview,
    PendingInfo,
    Approved,
    Declined,
    Withdrawn
}

public enum ReferralTrigger
{
    BeginReview,
    RequestInfo,
    InfoReceived,
    Approve,
    Decline,
    Withdraw
}

public sealed class ReferralStateMachineDefinition : IStateMachineDefinition<ReferralState, ReferralTrigger>
{
    public void Configure(StateMachineBuilder<ReferralState, ReferralTrigger> builder)
    {
        builder.State(ReferralState.Open)
            .Permit(ReferralTrigger.BeginReview, ReferralState.UnderReview)
            .Permit(ReferralTrigger.Withdraw, ReferralState.Withdrawn);

        builder.State(ReferralState.UnderReview)
            .Permit(ReferralTrigger.RequestInfo, ReferralState.PendingInfo)
            .Permit(ReferralTrigger.Approve, ReferralState.Approved)
            .Permit(ReferralTrigger.Decline, ReferralState.Declined)
            .Permit(ReferralTrigger.Withdraw, ReferralState.Withdrawn);

        builder.State(ReferralState.PendingInfo)
            .Permit(ReferralTrigger.InfoReceived, ReferralState.UnderReview)
            .Permit(ReferralTrigger.Withdraw, ReferralState.Withdrawn);

        builder.State(ReferralState.Approved).IsTerminal();
        builder.State(ReferralState.Declined).IsTerminal();
        builder.State(ReferralState.Withdrawn).IsTerminal();
    }
}

