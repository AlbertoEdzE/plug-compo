namespace KSquare.ProposalOrchestrator.Exceptions;

public sealed class ProposalTemplateNotFoundException(string proposalType)
    : Exception($"Template ID not found for proposal type '{proposalType}'.")
{
    public string ProposalType { get; } = proposalType;
}

