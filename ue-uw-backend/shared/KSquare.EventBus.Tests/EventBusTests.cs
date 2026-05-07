using System.Data;
using FluentAssertions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.EventBus.Providers;
using KSquare.EventBus.Tests.Synthesizers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.EventBus.Tests;

public sealed class EventBusTests
{
    [Fact]
    public async Task Publish_delivers_to_consumer_with_correct_payload()
    {
        var synthesizer = new EventBusDataSynthesizer();
        var topic = synthesizer.Topic();
        var subscription = synthesizer.Subscription();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ReceivedEvents>();

        services.AddKsEventBus(options =>
        {
            options.Provider = EventBusProvider.InMemory;
            options.UseOutbox = false;
        })
        .AddConsumer<TestMessage, RecordingConsumer>(topic, subscription);

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IEventPublisher>();
        var recorder = sp.GetRequiredService<ReceivedEvents>();

        var messageId = synthesizer.MessageId();
        var correlationId = synthesizer.CorrelationId();
        var eventType = synthesizer.EventType();

        await publisher.PublishAsync(
            topic,
            eventType,
            new TestMessage("hello"),
            new EventPublishOptions { MessageId = messageId, CorrelationId = correlationId }
        );

        recorder.Messages.Should().ContainSingle();
        recorder.Messages[0].MessageId.Should().Be(messageId);
        recorder.Messages[0].CorrelationId.Should().Be(correlationId);
        recorder.Messages[0].EventType.Should().Be(eventType);
        recorder.Messages[0].Payload.Value.Should().Be("hello");
    }

    [Fact]
    public async Task Duplicate_message_id_is_delivered_only_once()
    {
        var synthesizer = new EventBusDataSynthesizer();
        var topic = synthesizer.Topic();
        var subscription = synthesizer.Subscription();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ReceivedEvents>();

        services.AddKsEventBus(options =>
        {
            options.Provider = EventBusProvider.InMemory;
            options.UseOutbox = false;
        })
        .AddConsumer<TestMessage, RecordingConsumer>(topic, subscription);

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IEventPublisher>();
        var recorder = sp.GetRequiredService<ReceivedEvents>();

        var messageId = synthesizer.MessageId();
        var eventType = synthesizer.EventType();

        var options = new EventPublishOptions { MessageId = messageId, CorrelationId = synthesizer.CorrelationId() };

        await publisher.PublishAsync(topic, eventType, new TestMessage("one"), options);
        await publisher.PublishAsync(topic, eventType, new TestMessage("two"), options);

        recorder.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task Consumer_can_dead_letter_message_in_memory_provider()
    {
        var synthesizer = new EventBusDataSynthesizer();
        var topic = synthesizer.Topic();
        var subscription = synthesizer.Subscription();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKsEventBus(options =>
        {
            options.Provider = EventBusProvider.InMemory;
            options.UseOutbox = false;
        })
        .AddConsumer<TestMessage, DeadLetteringConsumer>(topic, subscription);

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IEventPublisher>();
        var provider = sp.GetRequiredService<InMemoryEventBusProvider>();

        await publisher.PublishAsync(
            topic,
            "test.deadletter",
            new TestMessage("poison"),
            new EventPublishOptions { MessageId = synthesizer.MessageId(), CorrelationId = synthesizer.CorrelationId() }
        );

        provider.DeadLetteredMessages.Should().ContainSingle();
        provider.DeadLetteredMessages.First().Reason.Should().Be("poison");
    }

    [Fact]
    public async Task Publish_with_outbox_creates_row_and_relay_delivers_message()
    {
        if (!await EnsureSqlReadyAsync())
        {
            return;
        }

        await ResetSqlTablesAsync();

        var synthesizer = new EventBusDataSynthesizer();
        var topic = synthesizer.Topic();
        var subscription = synthesizer.Subscription();
        var messageId = synthesizer.MessageId();
        var correlationId = synthesizer.CorrelationId();
        var eventType = synthesizer.EventType();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ReceivedEvents>();

        services.AddKsEventBus(options =>
        {
            options.Provider = EventBusProvider.InMemory;
            options.UseOutbox = true;
            options.OutboxConnectionString = SqlConnectionString;
            options.OutboxMaxRetries = 2;
            options.OutboxPollingInterval = TimeSpan.FromMilliseconds(50);
        })
        .AddConsumer<TestMessage, RecordingConsumer>(topic, subscription);

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IEventPublisher>();
        var relay = sp.GetRequiredService<IOutboxRelay>();
        var recorder = sp.GetRequiredService<ReceivedEvents>();

        await publisher.PublishAsync(
            topic,
            eventType,
            new TestMessage("hello-outbox"),
            new EventPublishOptions { MessageId = messageId, CorrelationId = correlationId }
        );

        (await CountOutboxByStatusAsync(OutboxStatus.Pending)).Should().Be(1);

        await relay.ProcessPendingAsync();

        (await CountOutboxByStatusAsync(OutboxStatus.Delivered)).Should().Be(1);
        recorder.Messages.Should().ContainSingle(m => m.MessageId == messageId);
    }

    private sealed record TestMessage(string Value);

    private sealed class ReceivedEvents
    {
        public List<EventContext<TestMessage>> Messages { get; } = new();
    }

    private sealed class RecordingConsumer(ReceivedEvents events) : IEventConsumer<TestMessage>
    {
        public Task ConsumeAsync(EventContext<TestMessage> context, CancellationToken ct = default)
        {
            events.Messages.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class DeadLetteringConsumer : IEventConsumer<TestMessage>
    {
        public async Task ConsumeAsync(EventContext<TestMessage> context, CancellationToken ct = default)
        {
            await context.DeadLetterAsync("poison", "test");
        }
    }

    private static async Task<bool> EnsureSqlReadyAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        Exception? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var conn = new SqlConnection(SqlConnectionString);
                await conn.OpenAsync();
                await EnsureSqlSchemaAsync(conn);
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500);
            }
        }

        _ = last;
        return false;
    }

    private static async Task EnsureSqlSchemaAsync(SqlConnection conn)
    {
        const string sql = """
            IF OBJECT_ID('dbo.outbox_messages', 'U') IS NULL
            BEGIN
                CREATE TABLE outbox_messages (
                    id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    topic NVARCHAR(256) NOT NULL,
                    event_type NVARCHAR(256) NOT NULL,
                    payload NVARCHAR(MAX) NOT NULL,
                    correlation_id NVARCHAR(128) NOT NULL,
                    message_id NVARCHAR(128) NULL,
                    properties NVARCHAR(MAX) NULL,
                    status INT NOT NULL,
                    retry_count INT NOT NULL,
                    last_error NVARCHAR(MAX) NULL,
                    created_at DATETIMEOFFSET NOT NULL,
                    processed_at DATETIMEOFFSET NULL
                );
                CREATE INDEX IX_outbox_messages_status ON outbox_messages(status);
                CREATE INDEX IX_outbox_messages_created_at ON outbox_messages(created_at);
            END;
            """;

        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ResetSqlTablesAsync()
    {
        await using var conn = new SqlConnection(SqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM outbox_messages;";
        cmd.CommandType = CommandType.Text;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountOutboxByStatusAsync(OutboxStatus status)
    {
        await using var conn = new SqlConnection(SqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM outbox_messages WHERE status = @status;";
        cmd.Parameters.AddWithValue("@status", (int)status);
        cmd.CommandType = CommandType.Text;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private const string SqlConnectionString =
        "Server=localhost,14333;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";
}
