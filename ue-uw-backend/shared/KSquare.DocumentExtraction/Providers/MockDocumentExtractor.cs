using KSquare.DocumentExtraction.Contracts;
using KSquare.DocumentExtraction.Models;

namespace KSquare.DocumentExtraction.Providers;

public sealed class MockDocumentExtractor : IDocumentExtractor
{
    public Task<ExtractionResult> ExtractAsync(DocumentInput input, string? modelHint = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        input.Validate();

        var fields = new List<ExtractedField>
        {
            new()
            {
                Name = "insured_name",
                Value = "Acme Inc",
                Confidence = 0.95f,
                PageNumber = 1
            }
        };

        var overall = fields.Average(f => f.Confidence);

        return Task.FromResult(new ExtractionResult
        {
            DocumentId = Guid.NewGuid().ToString("N"),
            ProviderOperationId = "mock",
            Status = fields.Any(f => f.Confidence < 0.75f) ? ExtractionStatus.PendingReview : ExtractionStatus.Succeeded,
            Fields = fields,
            Tables = [],
            Pages = [new ExtractedPage(1, 1000, 1400, "pixel")],
            DetectedDocumentType = modelHint,
            OverallConfidence = overall,
            ModelUsed = modelHint,
            CorrelationId = null
        });
    }
}
