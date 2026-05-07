namespace KSquare.BlobStorage.Exceptions;

public sealed class BlobNotFoundException(string blobPath) : Exception($"Blob not found: {blobPath}")
{
    public string BlobPath { get; } = blobPath;
}
