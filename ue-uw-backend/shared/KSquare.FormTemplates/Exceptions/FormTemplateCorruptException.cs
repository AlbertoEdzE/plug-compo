namespace KSquare.FormTemplates.Exceptions;

public sealed class FormTemplateCorruptException(string templateName, Exception? inner = null)
    : Exception($"Template '{templateName}' could not be read.", inner)
{
    public string TemplateName { get; } = templateName;
}

