using KSquare.Correlation.Contracts;
using KSquare.Correlation.Models;

namespace KSquare.Correlation;

public sealed class CorrelationContextAccessor : ICorrelationContextAccessor
{
    private static readonly AsyncLocal<CorrelationContext?> CurrentContext = new();

    public ICorrelationContext? Current
    {
        get => CurrentContext.Value;
        set
        {
            if (value is null)
            {
                CurrentContext.Value = null;
                return;
            }

            if (value is CorrelationContext correlationContext)
            {
                CurrentContext.Value = correlationContext;
                return;
            }

            CurrentContext.Value = new CorrelationContext(
                value.CorrelationId,
                value.TenantId,
                value.UserId
            );
        }
    }
}
