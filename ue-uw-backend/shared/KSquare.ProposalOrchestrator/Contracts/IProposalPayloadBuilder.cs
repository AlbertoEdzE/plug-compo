namespace KSquare.ProposalOrchestrator.Contracts;

using KSquare.ProposalOrchestrator.Models;

public interface IProposalPayloadBuilder
{
    ProposalProviderPayload Build(ProposalGenerationRequest request);
}

