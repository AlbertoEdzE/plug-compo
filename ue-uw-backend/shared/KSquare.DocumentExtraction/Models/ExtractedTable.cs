namespace KSquare.DocumentExtraction.Models;

public record ExtractedTable
{
    public required string TableName { get; init; }
    public required int PageNumber { get; init; }
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
    public float Confidence { get; init; }
}
