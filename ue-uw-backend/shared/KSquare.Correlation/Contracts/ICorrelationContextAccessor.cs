namespace KSquare.Correlation.Contracts;

public interface ICorrelationContextAccessor
{
    ICorrelationContext? Current { get; set; }
}
