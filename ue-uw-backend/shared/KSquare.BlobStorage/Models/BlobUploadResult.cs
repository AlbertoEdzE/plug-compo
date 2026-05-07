namespace KSquare.BlobStorage.Models;

public record BlobUploadResult(
    string BlobPath,
    string ContainerName,
    long SizeBytes,
    string ContentHash,
    DateTimeOffset UploadedAt
);
