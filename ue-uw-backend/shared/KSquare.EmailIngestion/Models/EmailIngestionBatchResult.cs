namespace KSquare.EmailIngestion.Models;

public record EmailIngestionBatchResult(
    int TotalFetched,
    int NewlyProcessed,
    int DuplicatesSkipped,
    int Errors,
    DateTimeOffset ProcessedAt
);
