using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Extensions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Database;
using KSquare.PolicyAdminAdapter.HostedService;
using KSquare.PolicyAdminAdapter.Models;
using KSquare.PolicyAdminAdapter.Providers.Pcas;
using KSquare.PolicyAdminAdapter.Validation;
using KSquare.PolicyAdminAdapter.Tests.Synthesizers;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KSquare.PolicyAdminAdapter.Tests;

public sealed class BindPollingHostedServiceTests
{
    [Fact]
    public async Task PollOnceAsync_on_issued_updates_job_and_publishes_policy_bound_event()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/v2/policies/pcas-txn-1/status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"status":"issued","policyNumber":"POL-123"}"""));

        var rules = new Mock<IRulesEngine>();
        rules.Setup(r => r.EvaluateAsync("bind-readiness", It.IsAny<BindReadinessContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuleEvaluationResult
            {
                RuleSetName = "bind-readiness",
                Results = Array.Empty<RuleResult>(),
                FiredActions = Array.Empty<string>()
            });

        var options = new PolicyAdminAdapterOptions
        {
            Provider = PolicyAdminProvider.Pcas,
            PcasBaseUrl = server.Url,
            PcasApiKey = "test-key",
            PollingInterval = TimeSpan.FromMilliseconds(10),
            MaxPollingAttempts = 100,
            BoundEventTopic = "policy-events",
            FailedEventTopic = "policy-events"
        };

        var queue = new ConcurrentQueue<PolicyBoundEvent>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        var dbRoot = new InMemoryDatabaseRoot();
        services.AddSingleton(dbRoot);
        services.AddDbContext<PolicyAdminDbContext>((sp, db) =>
        {
            db.UseInMemoryDatabase("kspl-policy-admin", sp.GetRequiredService<InMemoryDatabaseRoot>());
        });
        services.AddHttpClient("pcas", c => c.BaseAddress = new Uri(server.Url!));

        services.AddKsEventBus(o =>
        {
            o.Provider = EventBusProvider.InMemory;
            o.UseOutbox = false;
        });
        services.AddSingleton(queue);
        services.AddConsumer<PolicyBoundEvent, CapturingConsumer>(options.BoundEventTopic, "test-sub");

        services.AddKsAuditTrail(o =>
        {
            o.Provider = AuditProvider.InMemory;
            o.ServiceName = "kspl-policy-admin-tests";
        });

        services.AddScoped<IRulesEngine>(_ => rules.Object);
        services.AddSingleton<IPolicyAdminPayloadBuilder, PcasPayloadBuilder>();
        services.AddSingleton<IBindReadinessValidator, RulesEngineBindValidator>();
        services.AddScoped<PcasBindAdapter>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<PolicyAdminDbContext>();
        var synth = new PolicyAdminDataSynthesizer(seed: 11);
        var req = synth.BindRequest();
        var payload = new PcasPayloadBuilder(options).Build(req);
        var payloadJson = JsonSerializer.Serialize(payload.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var record = new BindJobRecord
        {
            BindJobId = "bind-1",
            QuoteId = req.QuoteId,
            SubmissionId = req.SubmissionId,
            Provider = PolicyAdminProvider.Pcas,
            ProviderTransactionId = "pcas-txn-1",
            Status = BindJobStatus.Processing,
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow.AddMilliseconds(-30)
        };

        db.BindJobs.Add(record);
        await db.SaveChangesAsync();

        var hosted = new BindPollingHostedService(sp.GetRequiredService<IServiceScopeFactory>(), options, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BindPollingHostedService>>());
        await hosted.PollOnceAsync(CancellationToken.None);

        using var scope2 = sp.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<PolicyAdminDbContext>();
        var stored = await db2.BindJobs.AsNoTracking().FirstOrDefaultAsync(x => x.BindJobId == "bind-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(BindJobStatus.Bound);
        stored.PolicyNumber.Should().Be("POL-123");

        queue.Should().ContainSingle(e => e.BindJobId == "bind-1" && e.PolicyNumber == "POL-123");
    }

    private sealed class CapturingConsumer(ConcurrentQueue<PolicyBoundEvent> queue) : IEventConsumer<PolicyBoundEvent>
    {
        public Task ConsumeAsync(EventContext<PolicyBoundEvent> context, CancellationToken ct = default)
        {
            queue.Enqueue(context.Payload);
            return Task.CompletedTask;
        }
    }
}
