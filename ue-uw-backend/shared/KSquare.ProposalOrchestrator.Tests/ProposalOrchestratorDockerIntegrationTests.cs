using System.Collections.Concurrent;
using System.Data;
using FluentAssertions;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Extensions;
using KSquare.Correlation.Extensions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Extensions;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Contracts;
using KSquare.ProposalOrchestrator.Database;
using KSquare.ProposalOrchestrator.Extensions;
using KSquare.ProposalOrchestrator.HostedService;
using KSquare.ProposalOrchestrator.Models;
using KSquare.ProposalOrchestrator.Tests.Synthesizers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KSquare.ProposalOrchestrator.Tests;

public sealed class ProposalOrchestratorDockerIntegrationTests
{
    [Fact]
    public async Task Job_submitted_then_polled_then_completed_publishes_event_and_persists_status()
    {
        if (!await EnsureSqlReadyAsync())
        {
            return;
        }

        await ResetSqlTablesAsync();

        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/v3/documents/generate").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"jobId":"gd-job-int-1","status":"queued"}"""));

        server.Given(Request.Create().WithPath("/api/v3/documents/gd-job-int-1/status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"status":"completed","downloadUrl":"{{server.Url}}/download/proposal.pdf"}"""));

        var pdfBytes = Enumerable.Range(0, 256).Select(i => (byte)(255 - i)).ToArray();
        server.Given(Request.Create().WithPath("/download/proposal.pdf").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/pdf").WithBody(pdfBytes));

        var synth = new ProposalOrchestratorDataSynthesizer(seed: 2026);
        var request = synth.Request(coverageLines: 3);

        var root = Path.Combine(Path.GetTempPath(), "kspl-proposals-it-" + Guid.NewGuid().ToString("N"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsCorrelation();

        services.AddKsBlobStorage(blob =>
        {
            blob.Provider = BlobProvider.LocalFileSystem;
            blob.LocalRootPath = root;
        });

        services.AddKsEventBus(bus =>
        {
            bus.Provider = EventBusProvider.InMemory;
            bus.UseOutbox = false;
        });

        services.AddSingleton<ConcurrentQueue<ProposalGenerationCompletedEvent>>();
        services.AddConsumer<ProposalGenerationCompletedEvent, CapturingConsumer>("proposal-events", "integration-sub");

        services.AddKsIdempotency(idem =>
        {
            idem.Provider = IdempotencyProvider.InMemory;
        });

        services.AddKsProposalOrchestrator(options =>
        {
            options.Provider = ProposalProvider.GhostDraft;
            options.ConnectionString = SqlConnectionString;
            options.GhostDraftApiUrl = server.Url;
            options.GhostDraftApiKey = "integration";
            options.OutputBlobContainer = "generated-proposals";
            options.OutputPathTemplate = "proposals/{year}/{month}/{quoteId}/{proposalType}-{timestamp}.pdf";
            options.CompletionEventTopic = "proposal-events";
            options.PollingInterval = TimeSpan.FromMilliseconds(10);
            options.MaxPollingAttempts = 10;
            options.MaxRetryAttempts = 1;
            options.TemplateIdMap[request.ProposalType] = "integration-template";
        });

        var sp = services.BuildServiceProvider();

        var orchestrator = sp.GetRequiredService<IProposalOrchestrator>();
        var started = await orchestrator.StartGenerationAsync(request);

        var hosted = sp.GetServices<IHostedService>().OfType<ProposalPollingHostedService>().Single();
        await hosted.RunOnceAsync(CancellationToken.None);

        var queue = sp.GetRequiredService<ConcurrentQueue<ProposalGenerationCompletedEvent>>();
        queue.Should().ContainSingle(e => e.JobId == started.JobId && e.QuoteId == request.QuoteId);

        var evt = queue.Single();
        var blobs = sp.GetRequiredService<IBlobStorageConnector>();
        (await blobs.ExistsAsync(evt.BlobPath)).Should().BeTrue();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProposalDbContext>();
        var stored = await db.ProposalJobs.AsNoTracking().FirstOrDefaultAsync(x => x.JobId == started.JobId);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(ProposalJobStatus.Completed);
        stored.ArtifactBlobPath.Should().Be(evt.BlobPath);
    }

    private sealed class CapturingConsumer(ConcurrentQueue<ProposalGenerationCompletedEvent> queue) : IEventConsumer<ProposalGenerationCompletedEvent>
    {
        public Task ConsumeAsync(EventContext<ProposalGenerationCompletedEvent> context, CancellationToken ct = default)
        {
            queue.Enqueue(context.Payload);
            return Task.CompletedTask;
        }
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
                await Task.Delay(500);
            }
        }

        _ = last;
        return false;
    }

    private static async Task EnsureSqlSchemaAsync(SqlConnection conn)
    {
        const string sql = """
            IF OBJECT_ID('dbo.proposal_generation_jobs', 'U') IS NULL
            BEGIN
                CREATE TABLE proposal_generation_jobs (
                    job_id              NVARCHAR(64) NOT NULL PRIMARY KEY,
                    quote_id            NVARCHAR(64) NOT NULL,
                    submission_id       NVARCHAR(64) NOT NULL,
                    proposal_type       NVARCHAR(50) NOT NULL,
                    provider            NVARCHAR(50) NOT NULL,
                    provider_job_id     NVARCHAR(200) NULL,
                    status              NVARCHAR(30) NOT NULL DEFAULT 'Pending',
                    retry_count         INT NOT NULL DEFAULT 0,
                    artifact_blob_path  NVARCHAR(1000) NULL,
                    artifact_sas_url    NVARCHAR(2000) NULL,
                    error_message       NVARCHAR(MAX) NULL,
                    created_at          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                    completed_at        DATETIMEOFFSET NULL
                );
                CREATE INDEX IX_proposal_quote ON proposal_generation_jobs (quote_id);
                CREATE INDEX IX_proposal_status ON proposal_generation_jobs (status, created_at);
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
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = "DELETE FROM proposal_generation_jobs;";
        await cmd.ExecuteNonQueryAsync();
    }

    private const string SqlConnectionString =
        "Server=localhost,14333;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";
}

