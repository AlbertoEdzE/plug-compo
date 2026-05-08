namespace KSquare.StateMachine.Models;

public sealed class ConcurrencyException : Exception
{
    public string EntityType { get; }
    public string EntityId { get; }

    public ConcurrencyException(string entityType, string entityId)
        : base($"Failed to transition {entityType}/{entityId} due to concurrent updates.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}

