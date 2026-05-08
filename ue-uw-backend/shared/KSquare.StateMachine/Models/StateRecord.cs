namespace KSquare.StateMachine.Models;

public sealed record StateRecord
{
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string CurrentState { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string UpdatedByActorId { get; init; }
    public int Version { get; init; } = 0;
}

