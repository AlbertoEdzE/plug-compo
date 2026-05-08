namespace KSquare.ExtractionMapper.Models;

public record FieldMappingRule
{
    public required string RuleId { get; init; }
    public required string CanonicalField { get; init; }
    public required IReadOnlyList<string> SourceFieldNames { get; init; }
    public required string TargetType { get; init; }
    public string? DefaultValue { get; init; }
    public bool Required { get; init; } = false;
    public string? TransformExpression { get; init; }
}

