namespace KSquare.FormTemplates.Models;

public sealed record FormTemplateDescriptor
{
    public required string TemplateName { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string OutputFormat { get; init; }
    public required IReadOnlyList<FormFieldDescriptor> Fields { get; init; }
}

