namespace KSquare.AuditTrail.Models;

public record AuditQuery(
    string? ResourceType = null,
    string? ResourceId = null,
    string? ActorUserId = null,
    string? Action = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50
);
