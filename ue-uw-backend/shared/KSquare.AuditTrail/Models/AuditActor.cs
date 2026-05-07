namespace KSquare.AuditTrail.Models;

public record AuditActor(
    string UserId,
    string DisplayName,
    string? Role = null,
    AuditActorType ActorType = AuditActorType.User
);
