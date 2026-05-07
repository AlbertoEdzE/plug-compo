using System.Text.Json;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Models;
using KSquare.EventBus.Publishers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KSquare.EventBus.Outbox;

internal sealed class OutboxRelay(
    EventBusOptions options,
    OutboxDbContext db,
    IDirectEventPublisher directPublisher,
    ILogger<OutboxRelay> logger
) : BackgroundService, IOutboxRelay
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingAsync(stoppingToken);
            await Task.Delay(options.OutboxPollingInterval, stoppingToken);
        }
    }

    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        if (!options.UseOutbox)
        {
            return;
        }

        var pending = await db.OutboxMessages
            .Where(x => x.Status == OutboxStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            ct.ThrowIfCancellationRequested();

            var publishOptions = new EventPublishOptions
            {
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId,
                Properties = message.Properties is null
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(message.Properties)
            };

            try
            {
                await directPublisher.PublishDirectAsync(message.Topic, message.EventType, message.Payload, publishOptions, ct);

                message.Status = OutboxStatus.Delivered;
                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.LastError = null;

                logger.LogInformation("Delivered outbox message {OutboxId} to topic {Topic}", message.Id, message.Topic);
            }
            catch (Exception ex)
            {
                message.RetryCount += 1;
                message.LastError = ex.Message;

                if (message.RetryCount >= options.OutboxMaxRetries)
                {
                    message.Status = OutboxStatus.Failed;
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                }

                logger.LogError(ex, "Failed to deliver outbox message {OutboxId} (retry {RetryCount})", message.Id, message.RetryCount);
            }
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
