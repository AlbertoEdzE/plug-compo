using System.Text.Json;
using Azure.Messaging.ServiceBus;
using KSquare.EventBus.Models;
using KSquare.EventBus.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KSquare.EventBus.Consumers;

public sealed class ServiceBusConsumerHost(
    AzureServiceBusProvider provider,
    IReadOnlyCollection<ConsumerRegistration> registrations,
    IServiceProvider serviceProvider,
    ILogger<ServiceBusConsumerHost> logger
) : BackgroundService
{
    private readonly List<ServiceBusProcessor> _processors = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var registration in registrations)
        {
            var processor = provider.Client.CreateProcessor(registration.Topic, registration.Subscription, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false
            });

            processor.ProcessMessageAsync += args => ProcessMessageAsync(registration, args, stoppingToken);
            processor.ProcessErrorAsync += args =>
            {
                logger.LogError(args.Exception, "Service Bus processor error for {Topic}/{Subscription}", registration.Topic, registration.Subscription);
                return Task.CompletedTask;
            };

            _processors.Add(processor);
            await processor.StartProcessingAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in _processors)
        {
            await processor.StopProcessingAsync(cancellationToken);
            await processor.DisposeAsync();
        }

        _processors.Clear();
        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(ConsumerRegistration registration, ProcessMessageEventArgs args, CancellationToken ct)
    {
        var messageId = args.Message.MessageId;
        var eventType = args.Message.Subject ?? args.Message.ApplicationProperties.GetValueOrDefault("eventType")?.ToString() ?? "unknown";
        var correlationId = args.Message.CorrelationId
            ?? args.Message.ApplicationProperties.GetValueOrDefault("correlationId")?.ToString()
            ?? Guid.NewGuid().ToString();

        var payloadJson = args.Message.Body.ToString();
        var payload = JsonSerializer.Deserialize(payloadJson, registration.MessageType);
        if (payload is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "deserialization_failed", "Unable to deserialize payload.");
            return;
        }

        var deadLettered = false;

        async Task DeadLetterAsync(string reason, string description)
        {
            deadLettered = true;
            await args.DeadLetterMessageAsync(args.Message, reason, description, cancellationToken: ct);
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var consumer = scope.ServiceProvider.GetRequiredService(registration.ConsumerType);

            var contextType = typeof(EventContext<>).MakeGenericType(registration.MessageType);
            var context = Activator.CreateInstance(contextType)!;

            contextType.GetProperty(nameof(EventContext<object>.MessageId))!.SetValue(context, messageId);
            contextType.GetProperty(nameof(EventContext<object>.EventType))!.SetValue(context, eventType);
            contextType.GetProperty(nameof(EventContext<object>.CorrelationId))!.SetValue(context, correlationId);
            contextType.GetProperty(nameof(EventContext<object>.Payload))!.SetValue(context, payload);
            contextType.GetProperty(nameof(EventContext<object>.EnqueuedAt))!.SetValue(context, args.Message.EnqueuedTime);
            contextType.GetProperty(nameof(EventContext<object>.DeliveryCount))!.SetValue(context, args.Message.DeliveryCount);
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

            if (!deadLettered)
            {
                await args.CompleteMessageAsync(args.Message, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Consumer {Consumer} failed for message {MessageId}", registration.ConsumerType.Name, messageId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
        }
    }
}
