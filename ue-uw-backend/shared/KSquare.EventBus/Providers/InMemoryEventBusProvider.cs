using System.Collections.Concurrent;
using System.Text.Json;
using KSquare.EventBus.Consumers;
using KSquare.EventBus.Models;
using KSquare.EventBus.Publishers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KSquare.EventBus.Providers;

public sealed class InMemoryEventBusProvider(
    IReadOnlyCollection<ConsumerRegistration> registrations,
    IServiceProvider serviceProvider,
    ILogger<InMemoryEventBusProvider> logger
) : IDirectEventPublisher
{
    private readonly ConcurrentDictionary<string, byte> _processed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<DeadLetteredMessage> _deadLettered = new();

    public IReadOnlyCollection<DeadLetteredMessage> DeadLetteredMessages => _deadLettered.ToArray();

    public async Task PublishDirectAsync(string topic, string eventType, string payloadJson, EventPublishOptions? options, CancellationToken ct)
    {
        var messageId = options?.MessageId ?? Guid.NewGuid().ToString("N");
        var correlationId = options?.CorrelationId ?? Guid.NewGuid().ToString();
        var dedupKey = $"{topic}:{messageId}";

        if (_processed.ContainsKey(dedupKey))
        {
            logger.LogInformation("Skipping duplicate message {MessageId} for topic {Topic}", messageId, topic);
            return;
        }

        var anyConsumers = false;
        foreach (var registration in registrations.Where(r => r.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            anyConsumers = true;

            var payload = JsonSerializer.Deserialize(payloadJson, registration.MessageType);
            if (payload is null)
            {
                throw new InvalidOperationException("Failed to deserialize payload.");
            }

            var deadLettered = false;

            Task DeadLetterAsync(string reason, string description)
            {
                deadLettered = true;
                _deadLettered.Enqueue(new DeadLetteredMessage(topic, eventType, messageId, reason, description));
                return Task.CompletedTask;
            }

            using var scope = serviceProvider.CreateScope();
            var consumer = scope.ServiceProvider.GetRequiredService(registration.ConsumerType);

            var contextType = typeof(EventContext<>).MakeGenericType(registration.MessageType);
            var context = Activator.CreateInstance(contextType)!;

            contextType.GetProperty(nameof(EventContext<object>.MessageId))!.SetValue(context, messageId);
            contextType.GetProperty(nameof(EventContext<object>.EventType))!.SetValue(context, eventType);
            contextType.GetProperty(nameof(EventContext<object>.CorrelationId))!.SetValue(context, correlationId);
            contextType.GetProperty(nameof(EventContext<object>.Payload))!.SetValue(context, payload);
            contextType.GetProperty(nameof(EventContext<object>.EnqueuedAt))!.SetValue(context, DateTimeOffset.UtcNow);
            contextType.GetProperty(nameof(EventContext<object>.DeliveryCount))!.SetValue(context, 1);
            contextType.GetProperty(nameof(EventContext<object>.DeadLetterAsync))!.SetValue(context, (Func<string, string, Task>)DeadLetterAsync);

            var method = registration.ConsumerType.GetMethod(
                "ConsumeAsync",
                new[] { contextType, typeof(CancellationToken) }
            );

            if (method is null)
            {
                throw new InvalidOperationException($"Consumer {registration.ConsumerType.Name} does not have the expected ConsumeAsync method.");
            }

            var task = (Task)method.Invoke(consumer, new[] { context, ct })!;
            await task;

            if (deadLettered)
            {
                logger.LogInformation("Message {MessageId} dead-lettered by consumer {Consumer}", messageId, registration.ConsumerType.Name);
            }
        }

        if (anyConsumers)
        {
            _processed.TryAdd(dedupKey, 0);
        }
    }

    public sealed record DeadLetteredMessage(string Topic, string EventType, string MessageId, string Reason, string Description);
}
