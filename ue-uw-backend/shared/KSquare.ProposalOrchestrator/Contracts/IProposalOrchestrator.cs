namespace KSquare.ProposalOrchestrator.Contracts;

using KSquare.ProposalOrchestrator.Models;

public interface IProposalOrchestrator
{
    Task<ProposalGenerationJob> StartGenerationAsync(ProposalGenerationRequest request, CancellationToken ct = default);

    Task<ProposalGenerationJob> GetJobStatusAsync(string jobId, CancellationToken ct = default);

    Task<ProposalArtifact> CompleteJobAsync(string jobId, string providerDocumentUrl, CancellationToken ct = default);
}

