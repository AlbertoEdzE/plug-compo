namespace KSquare.FormTemplates.Exceptions;

public sealed class FormTemplateNotFoundException(string templateName)
    : Exception($"Template '{templateName}' was not found.")
{
    public string TemplateName { get; } = templateName;
}

