namespace KSquare.StateMachine.Database;

public sealed class StateRecordEntity
{
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string CurrentState { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}

