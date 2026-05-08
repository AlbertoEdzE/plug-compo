using KSquare.DocumentExtraction.Models;

namespace KSquare.DocumentExtraction.Contracts;

public interface IDocumentExtractor
{
    Task<ExtractionResult> ExtractAsync(
        DocumentInput input,
        string? modelHint = null,
        CancellationToken ct = default
    );
}
