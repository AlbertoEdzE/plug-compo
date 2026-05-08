namespace KSquare.StateMachine.Contracts;

using KSquare.StateMachine.Models;

public interface IEntityStateMachine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    TState CurrentState { get; }

    bool CanFire(TTrigger trigger);

    Task FireAsync(TTrigger trigger, StateMachineContext context, CancellationToken ct = default);

    IReadOnlyList<TTrigger> PermittedTriggers { get; }
}

