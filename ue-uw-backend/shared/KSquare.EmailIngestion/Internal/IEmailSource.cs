namespace KSquare.EmailIngestion.Internal;

internal interface IEmailSource
{
    Task<IReadOnlyList<FetchedEmail>> FetchUnreadAsync(CancellationToken ct = default);
}

internal sealed record FetchedEmail(string SourceMessageId, byte[] RawMimeBytes, DateTimeOffset ReceivedAt);
