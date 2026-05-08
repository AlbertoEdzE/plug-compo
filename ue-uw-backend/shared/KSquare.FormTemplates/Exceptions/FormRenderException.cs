namespace KSquare.FormTemplates.Exceptions;

public sealed class FormRenderException(string message, Exception? inner = null) : Exception(message, inner);

