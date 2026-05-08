using System.Net;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using KSquare.EmailIngestion.Configuration;

namespace KSquare.EmailIngestion.Providers.MicrosoftGraph;

internal sealed class GraphEmailSource
{
    private readonly EmailIngestionOptions _options;
    private readonly GraphServiceClient _graph;

    public GraphEmailSource(EmailIngestionOptions options, GraphServiceClient graph)
    {
        _options = options;
        _graph = graph;
    }

    public async Task<IReadOnlyList<GraphEmailItem>> FetchUnreadAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.MailboxAddress))
        {
            throw new InvalidOperationException("MailboxAddress is required for MicrosoftGraph provider.");
        }

        var mailbox = _options.MailboxAddress;

        var retry = 0;
        while (true)
        {
            try
            {
                var messages = await _graph.Users[mailbox].MailFolders["Inbox"].Messages.GetAsync(request =>
                {
                    request.QueryParameters.Top = _options.MaxEmailsPerBatch;
                    request.QueryParameters.Filter = "isRead eq false";
                    request.QueryParameters.Select = new[] { "id", "subject", "receivedDateTime" };
                }, ct);

                var items = new List<GraphEmailItem>();
                foreach (var msg in messages?.Value ?? Enumerable.Empty<Message>())
                {
                    if (string.IsNullOrWhiteSpace(msg.Id))
                    {
                        continue;
                    }

                    var stream = await _graph.Users[mailbox].Messages[msg.Id].Content.GetAsync(cancellationToken: ct);
                    if (stream is null)
                    {
                        continue;
                    }

                    await using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    items.Add(new GraphEmailItem(msg.Id, ms.ToArray(), msg.ReceivedDateTime ?? DateTimeOffset.UtcNow));
                }

                return items;
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                retry++;
                if (retry >= 3)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retry)), ct);
            }
        }
    }

    public sealed record GraphEmailItem(string GraphMessageId, byte[] RawMimeBytes, DateTimeOffset ReceivedAt);
}
