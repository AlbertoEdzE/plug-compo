namespace KSquare.StateMachine.Models;

public sealed class InvalidTransitionException : Exception
{
    public string EntityType { get; }
    public string EntityId { get; }
    public string CurrentState { get; }
    public string AttemptedTrigger { get; }

    public InvalidTransitionException(string entityType, string entityId, string currentState, string trigger)
        : base($"Trigger '{trigger}' is not permitted from state '{currentState}' for {entityType}/{entityId}")
    {
        EntityType = entityType;
        EntityId = entityId;
        CurrentState = currentState;
        AttemptedTrigger = trigger;
    }
}

