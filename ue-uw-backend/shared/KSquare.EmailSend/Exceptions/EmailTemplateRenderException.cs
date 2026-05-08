namespace KSquare.EmailSend.Exceptions;

public sealed class EmailTemplateRenderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
