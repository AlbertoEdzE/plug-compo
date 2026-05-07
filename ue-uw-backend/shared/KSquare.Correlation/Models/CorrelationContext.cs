using KSquare.Correlation.Contracts;

namespace KSquare.Correlation.Models;

public record CorrelationContext(
    string CorrelationId,
    string? TenantId = null,
    string? UserId = null
) : ICorrelationContext;
