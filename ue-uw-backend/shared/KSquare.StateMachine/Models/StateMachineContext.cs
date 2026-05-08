namespace KSquare.StateMachine.Models;

public sealed record StateMachineContext
{
    public required string ActorId { get; init; }
    public required string ActorName { get; init; }
    public string? Reason { get; init; }
    public string? CorrelationId { get; init; }
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

