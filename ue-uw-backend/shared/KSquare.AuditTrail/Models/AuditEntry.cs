namespace KSquare.AuditTrail.Models;

public record AuditEntry
{
    public Guid EntryId { get; init; } = Guid.NewGuid();
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required string Action { get; init; }
    public required AuditActor Actor { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public string? CorrelationId { get; init; }
    public string? ServiceName { get; init; }
    public IDictionary<string, string>? Tags { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
