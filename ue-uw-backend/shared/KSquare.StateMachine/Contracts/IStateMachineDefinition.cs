namespace KSquare.StateMachine.Contracts;

using KSquare.StateMachine.Core;

public interface IStateMachineDefinition<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    void Configure(StateMachineBuilder<TState, TTrigger> builder);
}

