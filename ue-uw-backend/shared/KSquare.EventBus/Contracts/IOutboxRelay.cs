namespace KSquare.EventBus.Contracts;

public interface IOutboxRelay
{
    Task ProcessPendingAsync(CancellationToken ct = default);
}
