namespace KSquare.ProposalOrchestrator.Models;

public record ProposalArtifact
{
    public required string JobId { get; init; }
    public required string QuoteId { get; init; }
    public required string BlobPath { get; init; }
    public required string SasUrl { get; init; }
    public required DateTimeOffset SasExpiry { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSizeBytes { get; init; }
}

