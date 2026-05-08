using System.Collections.Concurrent;
using KSquare.ProposalOrchestrator.Contracts;
using KSquare.ProposalOrchestrator.Models;

namespace KSquare.ProposalOrchestrator.Providers;

public sealed class MockProposalOrchestrator : IProposalOrchestrator
{
    private readonly ConcurrentDictionary<string, ProposalArtifact> _artifacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProposalGenerationJob> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public Task<ProposalGenerationJob> StartGenerationAsync(ProposalGenerationRequest request, CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var blobPath = $"mock/proposals/{request.QuoteId}.pdf";

        var artifact = new ProposalArtifact
        {
            JobId = jobId,
            QuoteId = request.QuoteId,
            BlobPath = blobPath,
            SasUrl = "file:///mock/proposal.pdf",
            SasExpiry = DateTimeOffset.UtcNow.AddHours(24),
            FileName = $"{request.QuoteId}.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 0
        };

        var job = new ProposalGenerationJob
        {
            JobId = jobId,
            QuoteId = request.QuoteId,
            SubmissionId = request.SubmissionId,
            Status = ProposalJobStatus.Completed,
            ProviderJobId = "mock-job",
            ArtifactBlobPath = blobPath,
            ArtifactSasUrl = artifact.SasUrl,
            RetryCount = 0,
            ErrorMessage = null,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };

        _artifacts[jobId] = artifact;
        _jobs[jobId] = job;
        return Task.FromResult(job);
    }

    public Task<ProposalGenerationJob> GetJobStatusAsync(string jobId, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            return Task.FromResult(job);
        }

        throw new InvalidOperationException($"Job '{jobId}' not found.");
    }

    public Task<ProposalArtifact> CompleteJobAsync(string jobId, string providerDocumentUrl, CancellationToken ct = default)
    {
        if (_artifacts.TryGetValue(jobId, out var artifact))
        {
            return Task.FromResult(artifact);
        }

        throw new InvalidOperationException($"Job '{jobId}' not found.");
    }
}

