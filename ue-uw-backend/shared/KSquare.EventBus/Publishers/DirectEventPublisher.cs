using System.Text.Json;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Models;

namespace KSquare.EventBus.Publishers;

internal sealed class DirectEventPublisher(IDirectEventPublisher directPublisher) : IEventPublisher
{
    public async Task PublishAsync<T>(
        string topic,
        string eventType,
        T payload,
        EventPublishOptions? options = null,
        CancellationToken ct = default
    )
        where T : class
    {
        var json = JsonSerializer.Serialize(payload);
        await directPublisher.PublishDirectAsync(topic, eventType, json, options, ct);
    }

    public async Task PublishDirectAsync<T>(string topic, string eventType, T payload, CancellationToken ct = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(payload);
        await directPublisher.PublishDirectAsync(topic, eventType, json, null, ct);
    }
}
