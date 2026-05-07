namespace KSquare.Idempotency.Models;

public record IdempotencyResult(
    int StatusCode,
    string ResponseBody,
    string ContentType,
    DateTimeOffset ProcessedAt
);
