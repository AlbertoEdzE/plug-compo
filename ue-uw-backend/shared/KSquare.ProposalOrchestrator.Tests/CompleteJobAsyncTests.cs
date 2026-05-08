using System.Collections.Concurrent;
using FluentAssertions;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Extensions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Providers;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Contracts;
using KSquare.ProposalOrchestrator.Database;
using KSquare.ProposalOrchestrator.Mapping;
using KSquare.ProposalOrchestrator.Models;
using KSquare.ProposalOrchestrator.Providers;
using KSquare.ProposalOrchestrator.Tests.Synthesizers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KSquare.ProposalOrchestrator.Tests;

public sealed class CompleteJobAsyncTests
{
    [Fact]
    public async Task CompleteJobAsync_sets_completed_status_uploads_blob_and_publishes_event()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/v3/documents/generate").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"jobId":"gd-job-999","status":"queued"}"""));

        var pdfBytes = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        server.Given(Request.Create().WithPath("/download/proposal.pdf").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/pdf").WithBody(pdfBytes));

        var synth = new ProposalOrchestratorDataSynthesizer(seed: 7);
        var request = synth.Request(coverageLines: 2) with { OutputFormat = "pdf" };

        var options = new ProposalOrchestratorOptions
        {
            GhostDraftApiUrl = server.Url,
            GhostDraftApiKey = "test",
            OutputBlobContainer = "generated-proposals",
            OutputPathTemplate = "proposals/{year}/{month}/{quoteId}/{proposalType}-{timestamp}.pdf",
            CompletionEventTopic = "proposal-events"
        };
        options.TemplateIdMap[request.ProposalType] = "test-template";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddDbContext<ProposalDbContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddHttpClient("ghostdraft", c => c.BaseAddress = new Uri(server.Url!));
        services.AddSingleton<IProposalPayloadBuilder, GhostDraftPayloadBuilder>();

        var root = Path.Combine(Path.GetTempPath(), "kspl-proposals-" + Guid.NewGuid().ToString("N"));
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
        services.AddConsumer<ProposalGenerationCompletedEvent, CapturingConsumer>(options.CompletionEventTopic, "test-sub");

        services.AddSingleton<IIdempotencyGuard>(sp => new InMemoryIdempotencyGuard(new IdempotencyOptions { Provider = IdempotencyProvider.InMemory }));

        services.AddScoped<GhostDraftProposalOrchestrator>();
        services.AddScoped<IProposalOrchestrator>(sp => sp.GetRequiredService<GhostDraftProposalOrchestrator>());

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var orch = scope.ServiceProvider.GetRequiredService<IProposalOrchestrator>();

        var job = await orch.StartGenerationAsync(request);
        var artifact = await orch.CompleteJobAsync(job.JobId, $"{server.Url}/download/proposal.pdf");

        artifact.BlobPath.Should().StartWith(options.OutputBlobContainer + "/");
        artifact.ContentType.Should().Be("application/pdf");

        var db = scope.ServiceProvider.GetRequiredService<ProposalDbContext>();
        var stored = await db.ProposalJobs.AsNoTracking().FirstOrDefaultAsync(x => x.JobId == job.JobId);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(ProposalJobStatus.Completed);
        stored.ArtifactBlobPath.Should().NotBeNullOrWhiteSpace();

        var queue = sp.GetRequiredService<ConcurrentQueue<ProposalGenerationCompletedEvent>>();
        queue.Should().ContainSingle(e => e.JobId == job.JobId && e.QuoteId == request.QuoteId);
    }

    private sealed class CapturingConsumer(ConcurrentQueue<ProposalGenerationCompletedEvent> queue) : IEventConsumer<ProposalGenerationCompletedEvent>
    {
        public Task ConsumeAsync(EventContext<ProposalGenerationCompletedEvent> context, CancellationToken ct = default)
        {
            queue.Enqueue(context.Payload);
            return Task.CompletedTask;
        }
    }
}
