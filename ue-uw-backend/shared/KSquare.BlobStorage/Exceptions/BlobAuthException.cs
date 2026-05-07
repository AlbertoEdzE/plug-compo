namespace KSquare.BlobStorage.Exceptions;

public sealed class BlobAuthException(string message, Exception? innerException = null)
    : Exception(message, innerException);
