namespace KSquare.StateMachine.Contracts;

public interface IStateMachineFactory
{
    Task<IEntityStateMachine<TState, TTrigger>> LoadAsync<TState, TTrigger>(
        string entityType,
        string entityId,
        TState initialState,
        CancellationToken ct = default)
        where TState : struct, Enum
        where TTrigger : struct, Enum;
}

