using KSquare.AuditTrail.Contracts;
using KSquare.EventBus.Contracts;
using KSquare.StateMachine.Configuration;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Core;
using KSquare.StateMachine.Database;
using KSquare.StateMachine.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;

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

        var audit = new Mock<IAuditTrailWriter>();
        services.AddSingleton(audit);
        services.AddSingleton(audit.Object);

        var publisher = new Mock<IEventPublisher>();
        services.AddSingleton(publisher);
        services.AddSingleton(publisher.Object);

        services.AddScoped<IStateMachineFactory, StatelessStateMachineFactory>();

        services.AddSingleton<IStateMachineDefinition<SubmissionState, SubmissionTrigger>, SubmissionStateMachineDefinition>();
        services.AddSingleton<IStateMachineDefinition<QuoteState, QuoteTrigger>, QuoteStateMachineDefinition>();
        services.AddSingleton<IStateMachineDefinition<ReferralState, ReferralTrigger>, ReferralStateMachineDefinition>();

        return services.BuildServiceProvider();
    }
}

