namespace KSquare.BlobStorage.Exceptions;

public sealed class BlobSizeExceededException(long sizeBytes, long maxBytes)
    : Exception($"Upload size {sizeBytes} bytes exceeds maximum {maxBytes} bytes.")
{
    public long SizeBytes { get; } = sizeBytes;
    public long MaxBytes { get; } = maxBytes;
}
