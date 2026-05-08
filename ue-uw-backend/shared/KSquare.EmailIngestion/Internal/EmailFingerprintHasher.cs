using System.Security.Cryptography;
using System.Text;
using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Internal;

internal static class EmailFingerprintHasher
{
    public static string ComputeHash(EmailFingerprint fingerprint)
    {
        var normalizedFrom = Normalize(fingerprint.FromAddress);
        var normalizedSubject = NormalizeSubject(fingerprint.Subject);
        var dateBucket = Normalize(fingerprint.DateBucket);
        var contentHash = Normalize(fingerprint.ContentHash);

        var payload = $"{normalizedFrom}||{normalizedSubject}||{dateBucket}||{contentHash}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string NormalizeSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        var trimmed = subject.Trim();
        var collapsed = new string(trimmed.Select(c => char.IsWhiteSpace(c) ? ' ' : c).ToArray());
        while (collapsed.Contains("  ", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("  ", " ", StringComparison.Ordinal);
        }

        return collapsed.ToLowerInvariant();
    }

    public static string? Md5Of(byte[] bytes)
    {
        var md5 = MD5.HashData(bytes);
        return Convert.ToHexString(md5).ToLowerInvariant();
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
