using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.DocumentNarrative.Contracts;
using KSquare.DocumentNarrative.Models;

namespace KSquare.DocumentNarrative.Providers;

public sealed class DocumentNarrativeHttpClient(HttpClient http) : IDocumentNarrativeAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static DocumentNarrativeHttpClient()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
    }

    public async Task<NarrativeResult> GenerateNarrativeAsync(NarrativeRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("narrative/generate", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NarrativeResult>(JsonOptions, ct);
        if (result is null)
        {
            throw new InvalidOperationException("DocumentNarrative response was empty.");
        }

        return result;
    }
}
