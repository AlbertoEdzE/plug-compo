using KSquare.EventBus.Models;

namespace KSquare.EventBus.Contracts;

public interface IEventConsumer<TMessage>
    where TMessage : class
{
    Task ConsumeAsync(EventContext<TMessage> context, CancellationToken ct = default);
}
