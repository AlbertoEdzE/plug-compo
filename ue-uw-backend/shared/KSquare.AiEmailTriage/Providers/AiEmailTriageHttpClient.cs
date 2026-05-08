using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.AiEmailTriage.Contracts;
using KSquare.AiEmailTriage.Models;

namespace KSquare.AiEmailTriage.Providers;

public sealed class AiEmailTriageHttpClient(HttpClient http) : IAiEmailTriageAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<EmailTriageResult> TriageAsync(EmailTriageRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("email/triage", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmailTriageResult>(JsonOptions, ct);
        if (result is null)
        {
            throw new InvalidOperationException("AiEmailTriage response was empty.");
        }

        return result;
    }
}

