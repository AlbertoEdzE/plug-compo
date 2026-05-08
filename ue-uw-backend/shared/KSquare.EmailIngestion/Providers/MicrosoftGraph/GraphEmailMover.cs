using Microsoft.Graph;
using Microsoft.Graph.Models;
using KSquare.EmailIngestion.Configuration;

namespace KSquare.EmailIngestion.Providers.MicrosoftGraph;

internal sealed class GraphEmailMover
{
    private readonly EmailIngestionOptions _options;
    private readonly GraphServiceClient _graph;
    private string? _processedFolderId;

    public GraphEmailMover(EmailIngestionOptions options, GraphServiceClient graph)
    {
        _options = options;
        _graph = graph;
    }

    public async Task MarkReadAndMoveToProcessedAsync(string graphMessageId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.MailboxAddress))
        {
            throw new InvalidOperationException("MailboxAddress is required for MicrosoftGraph provider.");
        }

        var mailbox = _options.MailboxAddress;

        await _graph.Users[mailbox].Messages[graphMessageId].PatchAsync(
            new Message { IsRead = true },
            cancellationToken: ct
        );

        var processedId = await GetProcessedFolderIdAsync(ct);
        if (processedId is null)
        {
            return;
        }

        await _graph.Users[mailbox].Messages[graphMessageId].Move.PostAsync(
            new Microsoft.Graph.Users.Item.Messages.Item.Move.MovePostRequestBody
            {
                DestinationId = processedId
            },
            cancellationToken: ct
        );
    }

    private async Task<string?> GetProcessedFolderIdAsync(CancellationToken ct)
    {
        if (_processedFolderId is not null)
        {
            return _processedFolderId;
        }

        if (string.IsNullOrWhiteSpace(_options.MailboxAddress))
        {
            return null;
        }

        var processedName = _options.ProcessedFolderName ?? "Processed";
        var mailbox = _options.MailboxAddress;

        var folders = await _graph.Users[mailbox].MailFolders.GetAsync(request =>
        {
            request.QueryParameters.Select = new[] { "id", "displayName" };
        }, ct);

        var match = folders?.Value?.FirstOrDefault(f => f.DisplayName?.Equals(processedName, StringComparison.OrdinalIgnoreCase) == true);
        _processedFolderId = match?.Id;
        return _processedFolderId;
    }
}
