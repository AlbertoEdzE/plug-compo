namespace KSquare.IntelligentPrefill.Contracts;

using KSquare.IntelligentPrefill.Models;

public interface IIntelligentPrefillAdapter
{
    Task<PrefillResult> PrefillAsync(PrefillRequest request, CancellationToken ct = default);
}
