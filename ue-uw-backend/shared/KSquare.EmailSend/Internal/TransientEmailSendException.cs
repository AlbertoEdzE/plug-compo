namespace KSquare.EmailSend.Internal;

internal sealed class TransientEmailSendException(string message, Exception? innerException = null)
    : Exception(message, innerException);
