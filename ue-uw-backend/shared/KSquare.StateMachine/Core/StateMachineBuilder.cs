namespace KSquare.StateMachine.Core;

public sealed class StateMachineBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly Dictionary<TState, StateSpec<TState, TTrigger>> _states = new();

    public StateConfigurationBuilder<TState, TTrigger> State(TState state)
    {
        if (!_states.TryGetValue(state, out var spec))
        {
            spec = new StateSpec<TState, TTrigger>(state);
            _states[state] = spec;
        }

        return new StateConfigurationBuilder<TState, TTrigger>(spec);
    }

    internal IReadOnlyDictionary<TState, StateSpec<TState, TTrigger>> Build() => _states;
}

public sealed class StateConfigurationBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly StateSpec<TState, TTrigger> _spec;

    internal StateConfigurationBuilder(StateSpec<TState, TTrigger> spec)
    {
        _spec = spec;
    }

    public StateConfigurationBuilder<TState, TTrigger> Permit(TTrigger trigger, TState destination)
    {
        _spec.Permits[trigger] = destination;
        return this;
    }

    public StateConfigurationBuilder<TState, TTrigger> PermitReentry(TTrigger trigger)
    {
        _spec.ReentryPermits.Add(trigger);
        return this;
    }

    public void IsTerminal()
    {
        _spec.IsTerminal = true;
    }
}

internal sealed class StateSpec<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    public TState State { get; }
    public Dictionary<TTrigger, TState> Permits { get; } = new();
    public HashSet<TTrigger> ReentryPermits { get; } = new();
    public bool IsTerminal { get; set; }

    public StateSpec(TState state)
    {
        State = state;
    }
}
