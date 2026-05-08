namespace KSquare.StateMachine.Definitions;

using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Core;

public enum SubmissionState
{
    Draft,
    Submitted,
    InReview,
    Referred,
    Approved,
    Declined,
    Withdrawn
}

public enum SubmissionTrigger
{
    Submit,
    BeginReview,
    Refer,
    Approve,
    Decline,
    Withdraw,
    ReturnToReview
}

public sealed class SubmissionStateMachineDefinition : IStateMachineDefinition<SubmissionState, SubmissionTrigger>
{
    public void Configure(StateMachineBuilder<SubmissionState, SubmissionTrigger> builder)
    {
        builder.State(SubmissionState.Draft)
            .Permit(SubmissionTrigger.Submit, SubmissionState.Submitted)
            .Permit(SubmissionTrigger.Withdraw, SubmissionState.Withdrawn);

        builder.State(SubmissionState.Submitted)
            .Permit(SubmissionTrigger.BeginReview, SubmissionState.InReview)
            .Permit(SubmissionTrigger.Withdraw, SubmissionState.Withdrawn);

        builder.State(SubmissionState.InReview)
            .Permit(SubmissionTrigger.Refer, SubmissionState.Referred)
            .Permit(SubmissionTrigger.Approve, SubmissionState.Approved)
            .Permit(SubmissionTrigger.Decline, SubmissionState.Declined)
            .Permit(SubmissionTrigger.Withdraw, SubmissionState.Withdrawn);

        builder.State(SubmissionState.Referred)
            .Permit(SubmissionTrigger.ReturnToReview, SubmissionState.InReview)
            .Permit(SubmissionTrigger.Approve, SubmissionState.Approved)
            .Permit(SubmissionTrigger.Decline, SubmissionState.Declined)
            .Permit(SubmissionTrigger.Withdraw, SubmissionState.Withdrawn);

        builder.State(SubmissionState.Approved).IsTerminal();
        builder.State(SubmissionState.Declined).IsTerminal();
        builder.State(SubmissionState.Withdrawn).IsTerminal();
    }
}

