using KSquare.EventBus.Models;

namespace KSquare.EventBus.Contracts;

public interface IEventPublisher
{
    Task PublishAsync<T>(
        string topic,
        string eventType,
        T payload,
        EventPublishOptions? options = null,
        CancellationToken ct = default
    )
        where T : class;

    Task PublishDirectAsync<T>(
        string topic,
        string eventType,
        T payload,
        CancellationToken ct = default
    )
        where T : class;
}
