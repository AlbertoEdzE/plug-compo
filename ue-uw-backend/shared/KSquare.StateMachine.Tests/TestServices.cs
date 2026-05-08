using System.Collections.Concurrent;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Extensions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.StateMachine.Configuration;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Core;
using KSquare.StateMachine.Database;
using KSquare.StateMachine.Definitions;
using KSquare.StateMachine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.StateMachine.Tests;

internal static class TestServices
{
    public static ServiceProvider Build(InMemoryDatabaseRoot dbRoot, Action<StateMachineOptions>? configure = null)
    {
        var options = new StateMachineOptions
        {
            Provider = StateMachineProvider.Stateless,
            PublishTransitionEvents = true,
            WriteAuditTrail = true,
            TransitionEventTopic = "state-transitions",
            ConcurrencyRetryAttempts = 3
        };
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton(options);
        services.AddDbContext<StateMachineDbContext>((sp, db) =>
        {
            db.UseInMemoryDatabase("kspl-state-machine", dbRoot);
        });

        services.AddKsAuditTrail(audit =>
        {
            audit.Provider = AuditProvider.InMemory;
            audit.ServiceName = "kspl-state-machine-tests";
        });

        services.AddKsEventBus(bus =>
        {
            bus.Provider = EventBusProvider.InMemory;
            bus.UseOutbox = false;
        });

        services.AddSingleton(new ConcurrentQueue<StateTransitionedEvent>());
        services.AddConsumer<StateTransitionedEvent, CapturingConsumer>(options.TransitionEventTopic, "test-sub");

        services.AddScoped<IStateMachineFactory, StatelessStateMachineFactory>();

        services.AddSingleton<IStateMachineDefinition<SubmissionState, SubmissionTrigger>, SubmissionStateMachineDefinition>();
        services.AddSingleton<IStateMachineDefinition<QuoteState, QuoteTrigger>, QuoteStateMachineDefinition>();
        services.AddSingleton<IStateMachineDefinition<ReferralState, ReferralTrigger>, ReferralStateMachineDefinition>();

        return services.BuildServiceProvider();
    }

    private sealed class CapturingConsumer(ConcurrentQueue<StateTransitionedEvent> queue) : IEventConsumer<StateTransitionedEvent>
    {
        public Task ConsumeAsync(EventContext<StateTransitionedEvent> context, CancellationToken ct = default)
        {
            _ = ct;
            queue.Enqueue(context.Payload);
            return Task.CompletedTask;
        }
    }
}
