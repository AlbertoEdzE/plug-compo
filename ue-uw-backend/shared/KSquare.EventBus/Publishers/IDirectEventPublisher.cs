using KSquare.EventBus.Models;

namespace KSquare.EventBus.Publishers;

internal interface IDirectEventPublisher
{
    Task PublishDirectAsync(string topic, string eventType, string payloadJson, EventPublishOptions? options, CancellationToken ct);
}
