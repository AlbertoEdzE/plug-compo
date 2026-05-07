namespace KSquare.BlobStorage.Models;

public record BlobUploadRequest(
    string ContainerName,
    string BlobPath,
    Stream Content,
    string ContentType,
    IDictionary<string, string>? Metadata = null,
    BlobTier Tier = BlobTier.Hot
);
