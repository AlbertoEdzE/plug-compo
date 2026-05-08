using KSquare.EmailIngestion.Internal;

namespace KSquare.EmailIngestion.Providers.MicrosoftGraph;

internal sealed class GraphEmailMoverAdapter(GraphEmailMover mover) : IEmailMover
{
    public Task MarkReadAndMoveToProcessedAsync(string sourceMessageId, CancellationToken ct = default)
        => mover.MarkReadAndMoveToProcessedAsync(sourceMessageId, ct);
}
