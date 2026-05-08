namespace KSquare.DocumentExtraction.Models;

public record DocumentInput
{
    public string? BlobPath { get; init; }
    public Uri? DocumentUri { get; init; }
    public byte[]? Content { get; init; }

    public required string ContentType { get; init; }
    public string? FileName { get; init; }

    public void Validate()
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(BlobPath)) count++;
        if (DocumentUri is not null) count++;
        if (Content is not null && Content.Length > 0) count++;

        if (count != 1)
        {
            throw new InvalidOperationException("Exactly one of BlobPath, DocumentUri, or Content must be set.");
        }
    }
}
