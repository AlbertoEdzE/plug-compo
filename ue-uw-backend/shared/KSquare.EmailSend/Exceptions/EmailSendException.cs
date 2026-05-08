namespace KSquare.EmailSend.Exceptions;

public class EmailSendException(string message, Exception? innerException = null) : Exception(message, innerException);
