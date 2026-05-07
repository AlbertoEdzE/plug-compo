namespace KSquare.EventBus.Models;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Topic { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public required string CorrelationId { get; set; }
    public string? MessageId { get; set; }
    public string? Properties { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int RetryCount { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}
