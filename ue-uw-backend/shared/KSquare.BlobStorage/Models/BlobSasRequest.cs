namespace KSquare.BlobStorage.Models;

public record BlobSasRequest(
    string ContainerName,
    string BlobPath,
    BlobSasPermissions Permissions,
    TimeSpan Expiry,
    string? ContentDisposition = null
);
