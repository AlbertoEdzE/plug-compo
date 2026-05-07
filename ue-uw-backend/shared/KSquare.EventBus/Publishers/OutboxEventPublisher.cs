using System.Text.Json;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Models;
using KSquare.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KSquare.EventBus.Publishers;

internal sealed class OutboxEventPublisher(
    EventBusOptions options,
    OutboxDbContext outboxDb,
    IDirectEventPublisher directPublisher,
    ILogger<OutboxEventPublisher> logger
) : IEventPublisher
{
    public async Task PublishAsync<T>(
        string topic,
        string eventType,
        T payload,
        EventPublishOptions? publishOptions = null,
        CancellationToken ct = default
    )
        where T : class
    {
        if (!options.UseOutbox)
        {
            await PublishDirectAsync(topic, eventType, payload, ct);
            return;
        }

        var messageId = publishOptions?.MessageId;
        var correlationId = publishOptions?.CorrelationId ?? Guid.NewGuid().ToString();

        var outboxMessage = new OutboxMessage
        {
            Topic = topic,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            CorrelationId = correlationId,
            MessageId = messageId,
            Properties = publishOptions?.Properties is null ? null : JsonSerializer.Serialize(publishOptions.Properties),
            Status = OutboxStatus.Pending
        };

        outboxDb.OutboxMessages.Add(outboxMessage);
        await outboxDb.SaveChangesAsync(ct);

        logger.LogInformation("Enqueued outbox message {OutboxId} ({EventType}) to topic {Topic}", outboxMessage.Id, eventType, topic);
    }

    public async Task PublishDirectAsync<T>(string topic, string eventType, T payload, CancellationToken ct = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(payload);
        await directPublisher.PublishDirectAsync(topic, eventType, json, null, ct);
    }
}
