using FluentAssertions;
using KSquare.AuditTrail.Contracts;
using KSquare.EventBus.Contracts;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Database;
using KSquare.StateMachine.Definitions;
using KSquare.StateMachine.Models;
using KSquare.StateMachine.Tests.Synthesizers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace KSquare.StateMachine.Tests;

public sealed class SubmissionStateMachineTests
{
    [Fact]
    public async Task Submit_transitions_draft_to_submitted()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot);
        using var scope = sp.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", "sub-1", SubmissionState.Draft);

        var ctx = new StateMachineContextSynthesizer(seed: 1).Context();
        await machine.FireAsync(SubmissionTrigger.Submit, ctx);

        machine.CurrentState.Should().Be(SubmissionState.Submitted);
    }

    [Fact]
    public async Task Invalid_trigger_throws_invalid_transition_exception()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot);
        using var scope = sp.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", "sub-2", SubmissionState.Draft);

        var ctx = new StateMachineContextSynthesizer(seed: 2).Context();
        var act = async () => await machine.FireAsync(SubmissionTrigger.Approve, ctx);

        await act.Should().ThrowAsync<InvalidTransitionException>();
    }

    [Fact]
    public async Task Terminal_state_throws_on_any_trigger()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot);
        using var scope = sp.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", "sub-3", SubmissionState.Draft);
        var ctx = new StateMachineContextSynthesizer(seed: 3).Context();

        await machine.FireAsync(SubmissionTrigger.Submit, ctx);
        await machine.FireAsync(SubmissionTrigger.BeginReview, ctx);
        await machine.FireAsync(SubmissionTrigger.Approve, ctx);

        machine.CurrentState.Should().Be(SubmissionState.Approved);
        var act = async () => await machine.FireAsync(SubmissionTrigger.Withdraw, ctx);
        await act.Should().ThrowAsync<InvalidTransitionException>();
    }

    [Fact]
    public async Task Fire_writes_audit_trail_with_from_and_to_states()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot);
        using var scope = sp.CreateScope();

        var auditMock = scope.ServiceProvider.GetRequiredService<Mock<IAuditTrailWriter>>();
        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", "sub-4", SubmissionState.Draft);

        var ctx = new StateMachineContextSynthesizer(seed: 4).Context();
        await machine.FireAsync(SubmissionTrigger.Submit, ctx);

        auditMock.Verify(a => a.WriteAsync(
            It.Is<KSquare.AuditTrail.Models.AuditEntry>(e =>
                e.ResourceType == "Submission" &&
                e.ResourceId == "sub-4" &&
                e.Action == "StateTransition" &&
                e.Before == "Draft" &&
                e.After == "Submitted" &&
                e.Actor.UserId == ctx.ActorId
            ),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Fire_publishes_state_transitioned_event()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot);
        using var scope = sp.CreateScope();

        var publisherMock = scope.ServiceProvider.GetRequiredService<Mock<IEventPublisher>>();
        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", "sub-5", SubmissionState.Draft);

        var ctx = new StateMachineContextSynthesizer(seed: 5).Context();
        await machine.FireAsync(SubmissionTrigger.Submit, ctx);

        publisherMock.Verify(p => p.PublishAsync(
            "state-transitions",
            nameof(StateTransitionedEvent),
            It.Is<StateTransitionedEvent>(e =>
                e.EntityType == "Submission" &&
                e.EntityId == "sub-5" &&
                e.FromState == "Draft" &&
                e.ToState == "Submitted" &&
                e.Trigger == nameof(SubmissionTrigger.Submit)
            ),
            It.IsAny<KSquare.EventBus.Models.EventPublishOptions?>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Load_creates_state_record_on_first_call_and_restores_on_next_call()
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var sp = TestServices.Build(dbRoot);

        using (var scope = sp.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
            var machine = await factory.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", "sub-6", SubmissionState.Draft);
            machine.CurrentState.Should().Be(SubmissionState.Draft);

            var db = scope.ServiceProvider.GetRequiredService<StateMachineDbContext>();
            var record = await db.StateRecords.AsNoTracking().FirstOrDefaultAsync(x => x.EntityType == "Submission" && x.EntityId == "sub-6");
            record.Should().NotBeNull();
            record!.CurrentState.Should().Be("Draft");

            var ctx = new StateMachineContextSynthesizer(seed: 6).Context();
            await machine.FireAsync(SubmissionTrigger.Submit, ctx);
        }

        using (var scope2 = sp.CreateScope())
        {
            var factory2 = scope2.ServiceProvider.GetRequiredService<IStateMachineFactory>();
            var machine2 = await factory2.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", "sub-6", SubmissionState.Draft);
            machine2.CurrentState.Should().Be(SubmissionState.Submitted);
        }
    }
}

