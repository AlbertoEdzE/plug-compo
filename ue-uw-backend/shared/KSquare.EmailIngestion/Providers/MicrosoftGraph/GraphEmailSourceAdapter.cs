using KSquare.EmailIngestion.Internal;

namespace KSquare.EmailIngestion.Providers.MicrosoftGraph;

internal sealed class GraphEmailSourceAdapter(GraphEmailSource source) : IEmailSource
{
    public async Task<IReadOnlyList<FetchedEmail>> FetchUnreadAsync(CancellationToken ct = default)
    {
        var items = await source.FetchUnreadAsync(ct);
        return items.Select(i => new FetchedEmail(i.GraphMessageId, i.RawMimeBytes, i.ReceivedAt)).ToList();
    }
}
