using FluentAssertions;
using KSquare.BlobStorage.Contracts;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Extensions;
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

public sealed class StartGenerationAsyncTests
{
    [Fact]
    public async Task StartGenerationAsync_persists_job_with_pending_status_and_provider_job_id()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/v3/documents/generate").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"jobId":"gd-job-123","status":"queued"}"""));

        var synth = new ProposalOrchestratorDataSynthesizer(seed: 42);
        var request = synth.Request(coverageLines: 2);

        var options = new ProposalOrchestratorOptions
        {
            GhostDraftApiUrl = server.Url,
            GhostDraftApiKey = "test",
        };
        options.TemplateIdMap[request.ProposalType] = "test-template";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddDbContext<ProposalDbContext>(db => db.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddHttpClient("ghostdraft", c => c.BaseAddress = new Uri(server.Url!));
        services.AddSingleton<IProposalPayloadBuilder, GhostDraftPayloadBuilder>();

        services.AddKsEventBus(bus =>
        {
            bus.Provider = EventBusProvider.InMemory;
            bus.UseOutbox = false;
        });

        services.AddSingleton<IIdempotencyGuard>(sp => new InMemoryIdempotencyGuard(new IdempotencyOptions { Provider = IdempotencyProvider.InMemory }));
        services.AddSingleton<IBlobStorageConnector, NotUsedBlobStorageConnector>();

        services.AddScoped<GhostDraftProposalOrchestrator>();
        services.AddScoped<IProposalOrchestrator>(sp => sp.GetRequiredService<GhostDraftProposalOrchestrator>());

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var orch = scope.ServiceProvider.GetRequiredService<IProposalOrchestrator>();

        var job = await orch.StartGenerationAsync(request);

        job.Status.Should().Be(ProposalJobStatus.Pending);
        job.ProviderJobId.Should().Be("gd-job-123");

        var db = scope.ServiceProvider.GetRequiredService<ProposalDbContext>();
        var stored = await db.ProposalJobs.AsNoTracking().FirstOrDefaultAsync(x => x.JobId == job.JobId);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(ProposalJobStatus.Pending);
        stored.ProviderJobId.Should().Be("gd-job-123");
    }

    private sealed class NotUsedBlobStorageConnector : IBlobStorageConnector
    {
        public Task<KSquare.BlobStorage.Models.BlobUploadResult> UploadAsync(KSquare.BlobStorage.Models.BlobUploadRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KSquare.BlobStorage.Models.BlobDownloadResult> DownloadAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KSquare.BlobStorage.Models.BlobSasResult> GenerateSasUrlAsync(KSquare.BlobStorage.Models.BlobSasRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ArchiveAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<KSquare.BlobStorage.Models.BlobListItem> ListAsync(string prefix, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
