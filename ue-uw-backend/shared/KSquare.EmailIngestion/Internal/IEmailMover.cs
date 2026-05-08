namespace KSquare.EmailIngestion.Internal;

internal interface IEmailMover
{
    Task MarkReadAndMoveToProcessedAsync(string sourceMessageId, CancellationToken ct = default);
}
