namespace KSquare.ExtractionMapper.Models;

public record MappingWarning(
    string FieldName,
    string Message,
    WarningSeverity Severity
);

