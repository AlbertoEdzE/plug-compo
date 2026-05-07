namespace KSquare.BlobStorage.Models;

public enum BlobTier
{
    Hot,
    Cool,
    Archive
}

public enum BlobSasPermissions
{
    Read,
    Write,
    ReadWrite,
    Delete
}
