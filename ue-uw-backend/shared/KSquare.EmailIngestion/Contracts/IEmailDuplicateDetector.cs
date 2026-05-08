using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Contracts;

public interface IEmailDuplicateDetector
{
    Task<bool> IsDuplicateAsync(EmailFingerprint fingerprint, CancellationToken ct = default);
    Task MarkProcessedAsync(EmailFingerprint fingerprint, CancellationToken ct = default);
}
