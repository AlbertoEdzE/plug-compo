namespace KSquare.StateMachine.Models;

public sealed record StateTransitionedEvent
{
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string FromState { get; init; }
    public required string ToState { get; init; }
    public required string Trigger { get; init; }
    public required string ActorId { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? CorrelationId { get; init; }
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

