namespace KSquare.EmailSend.Models;

public record EmailAttachmentRef
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public byte[]? Content { get; init; }
    public string? BlobPath { get; init; }
}
