using KSquare.DocumentClassification.Contracts;
using KSquare.DocumentClassification.Models;

namespace KSquare.DocumentClassification.Providers;

public sealed class MockDocumentClassifier : IDocumentClassifier
{
    public Task<ClassificationResult> ClassifyAsync(DocumentInput input, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        input.Validate();

        return Task.FromResult(new ClassificationResult
        {
            DocumentType = "ACORD125",
            Confidence = 0.92f,
            Method = ClassificationMethod.AzureDocumentClassifier,
            CorrelationId = null
        });
    }
}
