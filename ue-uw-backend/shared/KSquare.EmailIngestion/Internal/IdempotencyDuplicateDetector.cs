using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Contracts;
using KSquare.EmailIngestion.Models;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Models;

namespace KSquare.EmailIngestion.Internal;

internal sealed class IdempotencyDuplicateDetector(EmailIngestionOptions options, IIdempotencyGuard idempotency) : IEmailDuplicateDetector
{
    private const string Prefix = "email-fp:";

    public async Task<bool> IsDuplicateAsync(EmailFingerprint fingerprint, CancellationToken ct = default)
    {
        var hash = EmailFingerprintHasher.ComputeHash(fingerprint);
        var key = $"{Prefix}{hash}";
        var result = await idempotency.GetAsync(key, ct);
        return result is not null;
    }

    public async Task MarkProcessedAsync(EmailFingerprint fingerprint, CancellationToken ct = default)
    {
        var hash = EmailFingerprintHasher.ComputeHash(fingerprint);
        var key = $"{Prefix}{hash}";

        var placeholder = new IdempotencyResult(
            204,
            "{}",
            "application/json",
            DateTimeOffset.UtcNow
        );

        await idempotency.SetAsync(key, placeholder, options.DuplicateDetectionWindow, ct);
    }
}
