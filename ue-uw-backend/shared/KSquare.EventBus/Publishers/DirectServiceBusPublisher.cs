using System.Text.Json;
using Azure.Messaging.ServiceBus;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Models;
using Microsoft.Extensions.Logging;

namespace KSquare.EventBus.Publishers;

public sealed class DirectServiceBusPublisher(
    EventBusOptions options,
    ServiceBusClient client,
    ILogger<DirectServiceBusPublisher> logger
) : IDirectEventPublisher
{
    public async Task PublishDirectAsync(string topic, string eventType, string payloadJson, EventPublishOptions? publishOptions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for AzureServiceBus provider.");
        }

        var messageId = publishOptions?.MessageId ?? Guid.NewGuid().ToString("N");
        var correlationId = publishOptions?.CorrelationId ?? Guid.NewGuid().ToString();

        var message = new ServiceBusMessage(payloadJson)
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            SessionId = publishOptions?.SessionId,
            ContentType = "application/json",
            Subject = eventType
        };

        message.ApplicationProperties["eventType"] = eventType;
        message.ApplicationProperties["correlationId"] = correlationId;

        if (publishOptions?.TimeToLive is not null)
        {
            message.TimeToLive = publishOptions.TimeToLive.Value;
        }

        if (publishOptions?.Properties is not null)
        {
            message.ApplicationProperties["properties"] = JsonSerializer.Serialize(publishOptions.Properties);
        }

        await using var sender = client.CreateSender(topic);
        await sender.SendMessageAsync(message, ct);

        logger.LogInformation("Published message {MessageId} to topic {Topic}", messageId, topic);
    }
}
