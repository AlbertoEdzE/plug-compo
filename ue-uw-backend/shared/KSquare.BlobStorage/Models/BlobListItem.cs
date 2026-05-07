namespace KSquare.BlobStorage.Models;

public record BlobListItem(
    string BlobPath,
    long SizeBytes,
    DateTimeOffset LastModified,
    IDictionary<string, string> Metadata
);
