using KSquare.DocumentClassification.Models;

namespace KSquare.DocumentClassification.Contracts;

public interface IDocumentClassifier
{
    Task<ClassificationResult> ClassifyAsync(
        DocumentInput input,
        CancellationToken ct = default
    );
}
