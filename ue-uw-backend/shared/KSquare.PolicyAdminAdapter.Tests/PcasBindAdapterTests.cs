using FluentAssertions;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Extensions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Extensions;
using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Database;
using KSquare.PolicyAdminAdapter.Models;
using KSquare.PolicyAdminAdapter.Providers.Pcas;
using KSquare.PolicyAdminAdapter.Validation;
using KSquare.PolicyAdminAdapter.Tests.Synthesizers;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KSquare.PolicyAdminAdapter.Tests;

public sealed class PcasBindAdapterTests
{
    [Fact]
    public async Task SubmitBindAsync_persists_bind_job_as_submitted_with_provider_transaction_id()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/v2/policies/bind").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"transactionId":"pcas-txn-123","status":"processing"}"""));

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
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddDbContext<PolicyAdminDbContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddHttpClient("pcas", c => c.BaseAddress = new Uri(server.Url!));

        services.AddKsEventBus(o =>
        {
            o.Provider = EventBusProvider.InMemory;
            o.UseOutbox = false;
        });

        services.AddKsAuditTrail(o =>
        {
            o.Provider = AuditProvider.InMemory;
            o.ServiceName = "kspl-policy-admin-tests";
        });

        services.AddScoped<IRulesEngine>(_ => rules.Object);
        services.AddSingleton<IPolicyAdminPayloadBuilder, PcasPayloadBuilder>();
        services.AddSingleton<IBindReadinessValidator, RulesEngineBindValidator>();
        services.AddScoped<PcasBindAdapter>();
        services.AddScoped<IPolicyAdminAdapter>(sp => sp.GetRequiredService<PcasBindAdapter>());

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IPolicyAdminAdapter>();

        var req = new PolicyAdminDataSynthesizer(seed: 10).BindRequest();
        var job = await adapter.SubmitBindAsync(req);

        job.Status.Should().Be(BindJobStatus.Processing);
        job.ProviderTransactionId.Should().Be("pcas-txn-123");

        var db = scope.ServiceProvider.GetRequiredService<PolicyAdminDbContext>();
        var stored = await db.BindJobs.AsNoTracking().FirstOrDefaultAsync(x => x.BindJobId == job.BindJobId);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(BindJobStatus.Processing);
        stored.ProviderTransactionId.Should().Be("pcas-txn-123");

        server.LogEntries.Should().ContainSingle();
        server.LogEntries.Single().RequestMessage.Headers["X-Api-Key"].FirstOrDefault().Should().Be("test-key");
    }
}
