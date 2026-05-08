using KSquare.AiEmailTriage.Models;

namespace KSquare.AiEmailTriage.Contracts;

public interface IAiEmailTriageAdapter
{
    Task<EmailTriageResult> TriageAsync(EmailTriageRequest request, CancellationToken ct = default);
}

