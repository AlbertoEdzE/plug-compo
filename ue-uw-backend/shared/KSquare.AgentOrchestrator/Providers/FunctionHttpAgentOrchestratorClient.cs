using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.AgentOrchestrator.Configuration;
using KSquare.AgentOrchestrator.Contracts;
using KSquare.AgentOrchestrator.Models;

namespace KSquare.AgentOrchestrator.Providers;

public sealed class FunctionHttpAgentOrchestratorClient(HttpClient http, AgentOrchestratorOptions options) : IAgentOrchestratorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(AgentChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var uri = BuildUri(options.FunctionBaseUrl, options.ChatRoute, options.FunctionKey);
        var payload = JsonSerializer.Serialize(ToWireRequest(request), JsonOptions);

        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].TrimStart();
            if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            AgentStreamEvent? ev = null;
            var parseError = false;
            try
            {
                ev = JsonSerializer.Deserialize<AgentStreamEvent>(data, JsonOptions);
                if (ev is null)
                {
                    continue;
                }
            }
            catch (JsonException)
            {
                parseError = true;
            }

            if (parseError)
            {
                yield return new AgentStreamEvent(Type: "ParseError", Error: "Failed to parse SSE event.");
                continue;
            }

            yield return ev!;
        }
    }

    public async Task SendFeedbackAsync(UserFeedback feedback, CancellationToken ct = default)
    {
        var uri = BuildUri(options.FunctionBaseUrl, options.FeedbackRoute, options.FunctionKey);
        var payload = JsonSerializer.Serialize(ToWireFeedback(feedback), JsonOptions);

        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
    }

    private static Uri BuildUri(string baseUrl, string route, string? functionKey)
    {
        var baseUri = new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/", UriKind.Absolute);
        var relative = route.TrimStart('/');

        var builder = new UriBuilder(new Uri(baseUri, relative));
        if (!string.IsNullOrWhiteSpace(functionKey))
        {
            var query = string.IsNullOrWhiteSpace(builder.Query) ? "" : builder.Query.TrimStart('?') + "&";
            builder.Query = $"{query}code={Uri.EscapeDataString(functionKey)}";
        }

        return builder.Uri;
    }

    private static object ToWireRequest(AgentChatRequest r)
    {
        return new
        {
            sessionId = r.SessionId,
            submissionId = r.SubmissionId,
            userId = r.UserId,
            userRole = r.UserRole,
            messages = r.Messages.Select(m => new
            {
                role = m.Role,
                content = m.Content,
                toolCallId = m.ToolCallId,
                toolName = m.ToolName
            }),
            correlationId = r.CorrelationId
        };
    }

    private static object ToWireFeedback(UserFeedback f)
    {
        return new
        {
            sessionId = f.SessionId,
            turnId = f.TurnId,
            userId = f.UserId,
            rating = f.Rating,
            comment = f.Comment
        };
    }
}
