namespace KSquare.EmailSend.Models;

public record EmailSendResult(
    bool Success,
    string? ProviderMessageId,
    string? Error,
    DateTimeOffset SentAt
);
