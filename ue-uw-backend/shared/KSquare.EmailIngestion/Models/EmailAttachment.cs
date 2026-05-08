namespace KSquare.EmailIngestion.Models;

public record EmailAttachment
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Content { get; init; }
    public long SizeBytes => Content.Length;
}
