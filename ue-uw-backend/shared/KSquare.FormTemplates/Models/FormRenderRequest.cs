namespace KSquare.FormTemplates.Models;

public sealed record FormRenderRequest
{
    public required string TemplateName { get; init; }
    public required IDictionary<string, string?> Fields { get; init; }
    public string? OutputFormat { get; init; } = "pdf";
    public string? CorrelationId { get; init; }
    public string? RelatedResourceId { get; init; }
}

