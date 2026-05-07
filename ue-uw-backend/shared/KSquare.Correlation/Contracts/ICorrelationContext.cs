namespace KSquare.Correlation.Contracts;

public interface ICorrelationContext
{
    string CorrelationId { get; }
    string? TenantId { get; }
    string? UserId { get; }
}
