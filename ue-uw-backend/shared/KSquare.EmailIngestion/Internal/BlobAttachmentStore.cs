using System.Text;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Contracts;
using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Internal;

internal sealed class BlobAttachmentStore(EmailIngestionOptions options, IBlobStorageConnector blob) : IEmailAttachmentStore
{
    public async Task<string> StoreAsync(EmailAttachment attachment, string correlationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var blobPath = ApplyTemplate(options.AttachmentPathTemplate, now, correlationId, SanitizeFileName(attachment.FileName));

        await using var stream = new MemoryStream(attachment.Content);
        var result = await blob.UploadAsync(new BlobUploadRequest(
            options.AttachmentContainerName,
            blobPath,
            stream,
            attachment.ContentType,
            new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["fileName"] = attachment.FileName,
                ["contentType"] = attachment.ContentType,
                ["source"] = "email-ingestion"
            }
        ), ct);

        return result.BlobPath;
    }

    public async Task<string> StoreRawEmailAsync(byte[] rawEmailBytes, DateTimeOffset receivedAt, string correlationId, CancellationToken ct = default)
    {
        var blobPath = ApplyTemplate(options.AttachmentPathTemplate, receivedAt, correlationId, "raw.eml");
        await using var stream = new MemoryStream(rawEmailBytes);

        var result = await blob.UploadAsync(new BlobUploadRequest(
            options.AttachmentContainerName,
            blobPath,
            stream,
            "message/rfc822",
            new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["fileName"] = "raw.eml",
                ["contentType"] = "message/rfc822",
                ["source"] = "email-ingestion"
            }
        ), ct);

        return result.BlobPath;
    }

    private static string ApplyTemplate(string template, DateTimeOffset at, string correlationId, string fileName)
    {
        return template
            .Replace("{year}", at.UtcDateTime.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("{month}", at.UtcDateTime.ToString("MM"), StringComparison.OrdinalIgnoreCase)
            .Replace("{day}", at.UtcDateTime.ToString("dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{correlationId}", correlationId, StringComparison.OrdinalIgnoreCase)
            .Replace("{fileName}", fileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "attachment";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }
}
