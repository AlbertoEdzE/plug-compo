namespace KSquare.FormTemplates.Models;

public sealed record FormFieldDescriptor(
    string PlaceholderName,
    string DisplayLabel,
    bool Required,
    string FieldType
);

