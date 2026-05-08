using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Internal;

namespace KSquare.EmailIngestion.Providers.HttpGraphStub;

internal sealed class HttpGraphEmailMover(EmailIngestionOptions options, HttpClient http) : IEmailMover
{
    public async Task MarkReadAndMoveToProcessedAsync(string sourceMessageId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.MailboxAddress))
        {
            return;
        }

        var mailbox = Uri.EscapeDataString(options.MailboxAddress);
        var messageId = Uri.EscapeDataString(sourceMessageId);

        await TryPatchIsReadAsync(mailbox, messageId, ct);
        await TryMoveAsync(mailbox, messageId, ct);
    }

    private async Task TryPatchIsReadAsync(string mailbox, string messageId, CancellationToken ct)
    {
        var url = $"/v1.0/users/{mailbox}/messages/{messageId}";
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        ApplyAuthHeader(request);

        var json = JsonSerializer.Serialize(new { isRead = true });
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        _ = response.StatusCode;
    }

    private async Task TryMoveAsync(string mailbox, string messageId, CancellationToken ct)
    {
        var processed = string.IsNullOrWhiteSpace(options.ProcessedFolderName) ? "Processed" : options.ProcessedFolderName!;
        var url = $"/v1.0/users/{mailbox}/messages/{messageId}/move";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuthHeader(request);

        var json = JsonSerializer.Serialize(new { destinationId = processed });
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        _ = response.StatusCode;
    }

    private void ApplyAuthHeader(HttpRequestMessage request)
    {
        var token = string.IsNullOrWhiteSpace(options.GraphAuthToken) ? "test-token" : options.GraphAuthToken!;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

