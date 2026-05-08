using KSquare.StateMachine.Configuration;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Database;
using KSquare.AuditTrail.Contracts;
using KSquare.EventBus.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stateless;

namespace KSquare.StateMachine.Core;

public sealed class StatelessStateMachineFactory(
    StateMachineOptions options,
    StateMachineDbContext db,
    IServiceProvider serviceProvider,
    IAuditTrailWriter audit,
    IEventPublisher publisher,
    ILogger<StatelessStateMachineFactory> logger,
    ILoggerFactory loggerFactory
) : IStateMachineFactory
{
    public async Task<IEntityStateMachine<TState, TTrigger>> LoadAsync<TState, TTrigger>(
        string entityType,
        string entityId,
        TState initialState,
        CancellationToken ct = default
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("EntityType is required.", nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("EntityId is required.", nameof(entityId));
        }

        var record = await db.StateRecords.FirstOrDefaultAsync(x => x.EntityType == entityType && x.EntityId == entityId, ct);
        if (record is null)
        {
            record = new StateRecordEntity
            {
                EntityType = entityType,
                EntityId = entityId,
                CurrentState = initialState.ToString(),
                Version = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = null
            };

            db.StateRecords.Add(record);
            await db.SaveChangesAsync(ct);
        }

        if (!Enum.TryParse<TState>(record.CurrentState, ignoreCase: true, out var current))
        {
            throw new InvalidOperationException($"Persisted state '{record.CurrentState}' cannot be parsed as {typeof(TState).Name} for {entityType}/{entityId}.");
        }

        var definition = serviceProvider.GetService(typeof(IStateMachineDefinition<TState, TTrigger>)) as IStateMachineDefinition<TState, TTrigger>;
        if (definition is null)
        {
            throw new InvalidOperationException($"No state machine definition registered for {typeof(TState).Name}/{typeof(TTrigger).Name}.");
        }

        var builder = new StateMachineBuilder<TState, TTrigger>();
        definition.Configure(builder);
        var spec = builder.Build();

        var stateHolder = new StateHolder<TState>(current);
        var sm = new StateMachine<TState, TTrigger>(() => stateHolder.State, s => stateHolder.State = s);

        foreach (var kvp in spec)
        {
            var cfg = sm.Configure(kvp.Key);
            if (kvp.Value.IsTerminal)
            {
                continue;
            }

            foreach (var trigger in kvp.Value.ReentryPermits)
            {
                cfg.PermitReentry(trigger);
            }

            foreach (var permit in kvp.Value.Permits)
            {
                cfg.Permit(permit.Key, permit.Value);
            }
        }

        logger.LogDebug("Loaded state machine for {EntityType}/{EntityId} at {State}", entityType, entityId, stateHolder.State);

        var entityLogger = loggerFactory.CreateLogger<StatelessEntityStateMachine<TState, TTrigger>>();
        return new StatelessEntityStateMachine<TState, TTrigger>(
            entityType,
            entityId,
            options,
            db,
            sm,
            stateHolder,
            audit,
            publisher,
            entityLogger
        );
    }
}

internal sealed class StateHolder<TState> where TState : struct, Enum
{
    public TState State { get; set; }

    public StateHolder(TState state)
    {
        State = state;
    }
}
