namespace KSquare.EventBus.Models;

public class EventContext<TMessage>
    where TMessage : class
{
    public required string MessageId { get; init; }
    public required string EventType { get; init; }
    public required string CorrelationId { get; init; }
    public required TMessage Payload { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }
    public int DeliveryCount { get; init; }
    public required Func<string, string, Task> DeadLetterAsync { get; init; }
}
