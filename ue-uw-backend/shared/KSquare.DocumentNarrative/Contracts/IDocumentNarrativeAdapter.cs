using KSquare.DocumentNarrative.Models;

namespace KSquare.DocumentNarrative.Contracts;

public interface IDocumentNarrativeAdapter
{
    Task<NarrativeResult> GenerateNarrativeAsync(NarrativeRequest request, CancellationToken ct = default);
}
