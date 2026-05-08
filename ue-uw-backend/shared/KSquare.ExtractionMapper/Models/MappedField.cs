namespace KSquare.ExtractionMapper.Models;

public record MappedField
{
    public required string CanonicalFieldName { get; init; }
    public required string? SourceFieldName { get; init; }
    public required object? Value { get; init; }
    public required float SourceConfidence { get; init; }
    public required string RuleApplied { get; init; }
}

