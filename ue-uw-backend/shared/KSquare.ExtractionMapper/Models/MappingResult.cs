namespace KSquare.ExtractionMapper.Models;

public record MappingResult<T>
{
    public required T Value { get; init; }
    public required IReadOnlyList<MappedField> MappedFields { get; init; }
    public required IReadOnlyList<MappingWarning> Warnings { get; init; }
    public bool HasLowConfidenceFields => MappedFields.Any(f => f.SourceConfidence < 0.75f);
    public bool HasUnmappedRequiredFields => Warnings.Any(w => w.Severity == WarningSeverity.RequiredFieldMissing);
}

