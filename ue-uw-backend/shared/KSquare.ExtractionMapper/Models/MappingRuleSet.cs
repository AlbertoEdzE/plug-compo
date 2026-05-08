namespace KSquare.ExtractionMapper.Models;

public record MappingRuleSet
{
    public required string DocumentType { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<FieldMappingRule> Rules { get; init; }
}

