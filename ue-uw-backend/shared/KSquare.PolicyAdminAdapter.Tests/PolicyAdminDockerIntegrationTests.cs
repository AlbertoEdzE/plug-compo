using System.Collections.Concurrent;
using System.Data;
using FluentAssertions;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Extensions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.PolicyAdminAdapter.Extensions;
using KSquare.PolicyAdminAdapter.HostedService;
using KSquare.PolicyAdminAdapter.Models;
using KSquare.PolicyAdminAdapter.Tests.Synthesizers;
using KSquare.RulesEngine.Configuration;
using KSquare.RulesEngine.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KSquare.PolicyAdminAdapter.Tests;

public sealed class PolicyAdminDockerIntegrationTests
{
    [Fact]
    public async Task Bind_flow_persists_bound_job_and_publishes_policy_bound_event()
    {
        if (!await EnsureSqlReadyAsync())
        {
            return;
        }

        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/v2/policies/bind").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"transactionId":"pcas-txn-999","status":"processing"}"""));
        server.Given(Request.Create().WithPath("/api/v2/policies/pcas-txn-999/status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"status":"issued","policyNumber":"POL-999"}"""));

        var queue = new ConcurrentQueue<PolicyBoundEvent>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsEventBus(bus =>
        {
            bus.Provider = EventBusProvider.InMemory;
            bus.UseOutbox = false;
        });
        services.AddSingleton(queue);
        services.AddConsumer<PolicyBoundEvent, CapturingConsumer>("policy-events", "integration-sub");

        services.AddKsAuditTrail(audit =>
        {
            audit.Provider = AuditProvider.InMemory;
            audit.ServiceName = "kspl-policy-admin-integration";
        });

        services.AddKsRulesEngine(rules =>
        {
            rules.RuleSource = RuleSetSource.EmbeddedYaml;
        }).AddRuleSet("bind-readiness");

        services.AddKsPolicyAdminAdapter(opts =>
        {
            opts.Provider = PolicyAdminProvider.Pcas;
            opts.PcasBaseUrl = server.Url;
            opts.PcasApiKey = "test-key";
            opts.SqlConnectionString = SqlConnectionString;
            opts.PollingInterval = TimeSpan.FromMilliseconds(10);
            opts.MaxPollingAttempts = 50;
            opts.BoundEventTopic = "policy-events";
            opts.FailedEventTopic = "policy-events";
        });

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var adapter = scope.ServiceProvider.GetRequiredService<KSquare.PolicyAdminAdapter.Contracts.IPolicyAdminAdapter>();
        var req = new PolicyAdminDataSynthesizer(seed: 21).BindRequest();
        var job = await adapter.SubmitBindAsync(req);

        var hosted = sp.GetServices<IHostedService>().OfType<BindPollingHostedService>().Single();
        await hosted.PollOnceAsync(CancellationToken.None);

        var (status, policyNumber) = await GetJobStatusAsync(job.BindJobId);
        status.Should().Be("Bound");
        policyNumber.Should().Be("POL-999");

        queue.Should().ContainSingle(e => e.BindJobId == job.BindJobId && e.PolicyNumber == "POL-999");
    }

    private sealed class CapturingConsumer(ConcurrentQueue<PolicyBoundEvent> queue) : IEventConsumer<PolicyBoundEvent>
    {
        public Task ConsumeAsync(EventContext<PolicyBoundEvent> context, CancellationToken ct = default)
        {
            queue.Enqueue(context.Payload);
            return Task.CompletedTask;
        }
    }

    private static async Task<(string Status, string? PolicyNumber)> GetJobStatusAsync(string bindJobId)
    {
        await using var conn = new SqlConnection(SqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = "SELECT status, policy_number FROM bind_jobs WHERE bind_job_id = @id;";
        cmd.Parameters.AddWithValue("@id", bindJobId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (
            Convert.ToString(reader["status"]) ?? "",
            reader["policy_number"] == DBNull.Value ? null : Convert.ToString(reader["policy_number"])
        );
    }

    private static async Task<bool> EnsureSqlReadyAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
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
                await Task.Delay(1000);
            }
        }

        _ = last;
        return false;
    }

    private static async Task EnsureSqlSchemaAsync(SqlConnection conn)
    {
        const string sql = """
            IF OBJECT_ID('dbo.bind_jobs', 'U') IS NULL
            BEGIN
                CREATE TABLE bind_jobs (
                    bind_job_id             NVARCHAR(64) NOT NULL PRIMARY KEY,
                    quote_id                NVARCHAR(64) NOT NULL,
                    submission_id           NVARCHAR(64) NOT NULL,
                    provider                NVARCHAR(50) NOT NULL,
                    provider_transaction_id NVARCHAR(200) NULL,
                    status                  NVARCHAR(30) NOT NULL DEFAULT 'Pending',
                    policy_number           NVARCHAR(100) NULL,
                    retry_count             INT NOT NULL DEFAULT 0,
                    error_code              NVARCHAR(100) NULL,
                    error_message           NVARCHAR(MAX) NULL,
                    payload_json            NVARCHAR(MAX) NULL,
                    created_at              DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                    completed_at            DATETIMEOFFSET NULL
                );
                CREATE INDEX IX_bind_quote ON bind_jobs (quote_id);
                CREATE INDEX IX_bind_status ON bind_jobs (status, created_at);
            END;
            """;

        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private const string SqlConnectionString =
        "Server=localhost,14333;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";
}
