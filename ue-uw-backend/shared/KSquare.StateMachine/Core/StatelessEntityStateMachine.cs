using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Models;
using KSquare.EventBus.Contracts;
using KSquare.StateMachine.Configuration;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Database;
using KSquare.StateMachine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stateless;

namespace KSquare.StateMachine.Core;

internal sealed class StatelessEntityStateMachine<TState, TTrigger>(
    string entityType,
    string entityId,
    StateMachineOptions options,
    StateMachineDbContext db,
    StateMachine<TState, TTrigger> machine,
    StateHolder<TState> stateHolder,
    IAuditTrailWriter audit,
    IEventPublisher publisher,
    ILogger<StatelessEntityStateMachine<TState, TTrigger>> logger
) : IEntityStateMachine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    public TState CurrentState => stateHolder.State;

    public bool CanFire(TTrigger trigger) => machine.CanFire(trigger);

    public IReadOnlyList<TTrigger> PermittedTriggers => machine.PermittedTriggers.ToArray();

    public async Task FireAsync(TTrigger trigger, StateMachineContext context, CancellationToken ct = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var now = DateTimeOffset.UtcNow;
        var maxAttempts = Math.Max(1, options.ConcurrencyRetryAttempts + 1);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var record = await db.StateRecords.FirstOrDefaultAsync(x => x.EntityType == entityType && x.EntityId == entityId, ct);
            if (record is null)
            {
                throw new InvalidOperationException($"State record not found for {entityType}/{entityId}.");
            }

            if (!Enum.TryParse<TState>(record.CurrentState, ignoreCase: true, out var loaded))
            {
                throw new InvalidOperationException($"Persisted state '{record.CurrentState}' cannot be parsed as {typeof(TState).Name} for {entityType}/{entityId}.");
            }

            stateHolder.State = loaded;

            if (!machine.CanFire(trigger))
            {
                throw new InvalidTransitionException(entityType, entityId, stateHolder.State.ToString(), trigger.ToString());
            }

            var fromState = stateHolder.State;
            machine.Fire(trigger);
            var toState = stateHolder.State;

            record.CurrentState = toState.ToString();
            record.UpdatedAt = now;
            record.UpdatedBy = context.ActorId;
            record.Version += 1;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();

                if (attempt >= maxAttempts - 1)
                {
                    throw new ConcurrencyException(entityType, entityId);
                }

                continue;
            }

            await TryWriteAuditAsync(fromState, toState, trigger, context, now, ct);
            await TryPublishEventAsync(fromState, toState, trigger, context, now, ct);
            return;
        }

        throw new ConcurrencyException(entityType, entityId);
    }

    private async Task TryWriteAuditAsync(TState fromState, TState toState, TTrigger trigger, StateMachineContext context, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (!options.WriteAuditTrail)
        {
            return;
        }

        try
        {
            var tags = new Dictionary<string, string>
            {
                ["fromState"] = fromState.ToString(),
                ["toState"] = toState.ToString(),
                ["trigger"] = trigger.ToString()
            };

            if (!string.IsNullOrWhiteSpace(context.Reason))
            {
                tags["reason"] = context.Reason;
            }

            foreach (var kvp in context.Metadata)
            {
                tags[kvp.Key] = kvp.Value;
            }

            await audit.WriteAsync(new AuditEntry
            {
                ResourceType = entityType,
                ResourceId = entityId,
                Action = "StateTransition",
                Actor = new AuditActor(context.ActorId, context.ActorName, ActorType: AuditActorType.User),
                Before = fromState.ToString(),
                After = toState.ToString(),
                CorrelationId = context.CorrelationId,
                ServiceName = "KSquare.StateMachine",
                Tags = tags,
                OccurredAt = occurredAt
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write audit trail for {EntityType}/{EntityId}", entityType, entityId);
        }
    }

    private async Task TryPublishEventAsync(TState fromState, TState toState, TTrigger trigger, StateMachineContext context, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (!options.PublishTransitionEvents)
        {
            return;
        }

        try
        {
            var evt = new StateTransitionedEvent
            {
                EntityType = entityType,
                EntityId = entityId,
                FromState = fromState.ToString(),
                ToState = toState.ToString(),
                Trigger = trigger.ToString(),
                ActorId = context.ActorId,
                Reason = context.Reason,
                OccurredAt = occurredAt,
                CorrelationId = context.CorrelationId,
                Metadata = new Dictionary<string, string>(context.Metadata)
            };

            await publisher.PublishAsync(options.TransitionEventTopic, nameof(StateTransitionedEvent), evt, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish transition event for {EntityType}/{EntityId}", entityType, entityId);
        }
    }
}

