namespace KSquare.BlobStorage.Models;

public record BlobSasResult(
    string SasUrl,
    DateTimeOffset ExpiresAt
);
