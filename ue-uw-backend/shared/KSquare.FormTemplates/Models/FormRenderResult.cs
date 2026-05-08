namespace KSquare.FormTemplates.Models;

public sealed record FormRenderResult
{
    public required byte[] Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
    public required string TemplateName { get; init; }
    public required string TemplateVersion { get; init; }
    public required IReadOnlyList<string> UnfilledRequiredFields { get; init; }
    public bool IsComplete => !UnfilledRequiredFields.Any();
    public DateTimeOffset RenderedAt { get; init; } = DateTimeOffset.UtcNow;
}

