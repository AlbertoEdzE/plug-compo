namespace KSquare.EmailSend.Exceptions;

public sealed class EmailTemplateNotFoundException(string templateName)
    : Exception($"Email template not found: {templateName}")
{
    public string TemplateName { get; } = templateName;
}
