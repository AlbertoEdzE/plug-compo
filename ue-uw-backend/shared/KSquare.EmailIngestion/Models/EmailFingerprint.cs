namespace KSquare.EmailIngestion.Models;

public record EmailFingerprint(
    string FromAddress,
    string Subject,
    string? DateBucket,
    string? ContentHash
);
