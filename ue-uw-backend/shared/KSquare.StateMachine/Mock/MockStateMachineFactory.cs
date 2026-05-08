using System.Collections.Concurrent;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Core;
using KSquare.StateMachine.Models;
using Stateless;

namespace KSquare.StateMachine.Mock;

public sealed class MockStateMachineFactory(IServiceProvider serviceProvider) : IStateMachineFactory
{
    private readonly ConcurrentDictionary<(string EntityType, string EntityId), string> _state = new();

    public Task<IEntityStateMachine<TState, TTrigger>> LoadAsync<TState, TTrigger>(
        string entityType,
        string entityId,
        TState initialState,
        CancellationToken ct = default
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        _ = ct;

        var key = (entityType, entityId);
        var stateString = _state.GetOrAdd(key, _ => initialState.ToString());
        if (!Enum.TryParse<TState>(stateString, ignoreCase: true, out var current))
        {
            current = initialState;
            _state[key] = current.ToString();
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

            foreach (var permit in kvp.Value.Permits)
            {
                cfg.Permit(permit.Key, permit.Value);
            }
        }

        return Task.FromResult<IEntityStateMachine<TState, TTrigger>>(new MockEntityStateMachine<TState, TTrigger>(key, _state, sm, stateHolder));
    }
}

internal sealed class MockEntityStateMachine<TState, TTrigger>(
    (string EntityType, string EntityId) key,
    ConcurrentDictionary<(string EntityType, string EntityId), string> store,
    StateMachine<TState, TTrigger> machine,
    StateHolder<TState> holder
) : IEntityStateMachine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    public TState CurrentState => holder.State;

    public bool CanFire(TTrigger trigger) => machine.CanFire(trigger);

    public IReadOnlyList<TTrigger> PermittedTriggers => machine.PermittedTriggers.ToArray();

    public Task FireAsync(TTrigger trigger, StateMachineContext context, CancellationToken ct = default)
    {
        _ = context;
        _ = ct;

        if (!machine.CanFire(trigger))
        {
            throw new InvalidTransitionException(key.EntityType, key.EntityId, holder.State.ToString(), trigger.ToString());
        }

        machine.Fire(trigger);
        store[key] = holder.State.ToString();
        return Task.CompletedTask;
    }
}

