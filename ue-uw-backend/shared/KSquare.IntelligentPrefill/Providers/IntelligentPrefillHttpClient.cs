using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.IntelligentPrefill.Contracts;
using KSquare.IntelligentPrefill.Models;

namespace KSquare.IntelligentPrefill.Providers;

public sealed class IntelligentPrefillHttpClient(HttpClient http) : IIntelligentPrefillAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<PrefillResult> PrefillAsync(PrefillRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("prefill/run", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PrefillResult>(JsonOptions, ct);
        if (result is null)
        {
            throw new InvalidOperationException("IntelligentPrefill response was empty.");
        }

        return result;
    }
}
