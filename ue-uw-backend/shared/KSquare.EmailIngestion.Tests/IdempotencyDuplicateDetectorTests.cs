using FluentAssertions;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Internal;
using KSquare.EmailIngestion.Models;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Providers;

namespace KSquare.EmailIngestion.Tests;

public sealed class IdempotencyDuplicateDetectorTests
{
    [Fact]
    public async Task IsDuplicateAsync_ShouldReturnFalseThenTrue_ForSameFingerprint()
    {
        var ingestionOptions = new EmailIngestionOptions
        {
            DuplicateDetectionWindow = TimeSpan.FromMinutes(10)
        };

        var idempotency = new InMemoryIdempotencyGuard(new IdempotencyOptions
        {
            Provider = IdempotencyProvider.InMemory,
            DefaultHttpKeyTtl = TimeSpan.FromMinutes(10)
        });

        var detector = new IdempotencyDuplicateDetector(ingestionOptions, idempotency);
        var fp = new EmailFingerprint("sender@example.com", "Subject", "2026-05-07", null);

        (await detector.IsDuplicateAsync(fp)).Should().BeFalse();
        await detector.MarkProcessedAsync(fp);
        (await detector.IsDuplicateAsync(fp)).Should().BeTrue();
    }
}
