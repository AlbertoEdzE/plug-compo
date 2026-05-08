using System.Net.Http.Headers;
using System.Text.Json;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Internal;

namespace KSquare.EmailIngestion.Providers.HttpGraphStub;

internal sealed class HttpGraphEmailSource(EmailIngestionOptions options, HttpClient http) : IEmailSource
{
    public async Task<IReadOnlyList<FetchedEmail>> FetchUnreadAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.GraphApiBaseUrl))
        {
            throw new InvalidOperationException("GraphApiBaseUrl must be set for HttpGraphStub provider.");
        }

        if (string.IsNullOrWhiteSpace(options.MailboxAddress))
        {
            throw new InvalidOperationException("MailboxAddress must be set for HttpGraphStub provider.");
        }

        var inbox = string.IsNullOrWhiteSpace(options.InboxFolderName) ? "Inbox" : options.InboxFolderName!;
        var url =
            $"/v1.0/users/{Uri.EscapeDataString(options.MailboxAddress)}/mailFolders/{Uri.EscapeDataString(inbox)}/messages" +
            $"?$top={options.MaxEmailsPerBatch}&$filter=isRead%20eq%20false&$select=id,receivedDateTime";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuthHeader(request);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<FetchedEmail>();
        }

        var results = new List<FetchedEmail>();
        foreach (var item in value.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idProp))
            {
                continue;
            }

            var id = idProp.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var receivedAt = DateTimeOffset.UtcNow;
            if (item.TryGetProperty("receivedDateTime", out var rdtProp) && rdtProp.ValueKind == JsonValueKind.String)
            {
                _ = DateTimeOffset.TryParse(rdtProp.GetString(), out receivedAt);
            }

            var raw = await FetchRawAsync(id, ct);
            results.Add(new FetchedEmail(id, raw, receivedAt));
        }

        return results;
    }

    private async Task<byte[]> FetchRawAsync(string messageId, CancellationToken ct)
    {
        var url = $"/v1.0/users/{Uri.EscapeDataString(options.MailboxAddress!)}/messages/{Uri.EscapeDataString(messageId)}/$value";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuthHeader(request);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private void ApplyAuthHeader(HttpRequestMessage request)
    {
        var token = string.IsNullOrWhiteSpace(options.GraphAuthToken) ? "test-token" : options.GraphAuthToken!;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

