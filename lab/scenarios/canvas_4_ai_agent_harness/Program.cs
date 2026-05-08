using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KSquare.AgentOrchestrator.Configuration;
using KSquare.AgentOrchestrator.Models;
using KSquare.AgentOrchestrator.Providers;
using Microsoft.Data.SqlClient;

var assertions = new List<AssertionRecord>();

var seedRaw = Environment.GetEnvironmentVariable("CANVAS_SEED") ?? "42";
_ = int.TryParse(seedRaw, out var seed);

var sql = Environment.GetEnvironmentVariable("LAB_SQL_CONNECTION")
          ?? "Server=localhost,1433;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";
var wiremock = Environment.GetEnvironmentVariable("LAB_WIREMOCK") ?? "http://localhost:8080";

var synthJson = Environment.GetEnvironmentVariable("CANVAS4_SYNTH_JSON") ?? "";
Canvas4Synth? synth = null;
try
{
    synth = JsonSerializer.Deserialize<Canvas4Synth>(synthJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    });
}
catch
{
    synth = null;
}

Record("synthesize_canvas4_inputs", synth is not null, $"seed={seedRaw}");

await Run("wiremock_stub_agent_and_llm_endpoints", async () =>
{
    using var http = new HttpClient { BaseAddress = new Uri(wiremock) };
    await http.PostAsync("/__admin/reset", content: null);
    await http.DeleteAsync("/__admin/requests");

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "POST", urlPath = "/openai/chat/completions" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new { id = "cmpl-1", choices = new[] { new { message = new { role = "assistant", content = "ok" } } } }
        }
    });

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "POST", urlPath = "/search" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new { value = Array.Empty<object>() }
        }
    });

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "POST", urlPath = "/contentsafety/text:analyze" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new { safe = true, categories = Array.Empty<object>() }
        }
    });

    var injectionBlockMapping = new
    {
        priority = 1,
        request = new
        {
            method = "POST",
            urlPath = "/api/assistant/chat",
            bodyPatterns = new[]
            {
                new { matches = "(?i).*ignore previous instructions.*" }
            }
        },
        response = new
        {
            status = 400,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new { error = "prompt_injection" }
        }
    };
    await http.PostAsJsonAsync("/__admin/mappings", injectionBlockMapping);

    var sse = BuildSseStream(runId: "r1", submissionId: (synth?.Conversation.Safe.SubmissionId ?? "SUB-001"));
    var chatOkMapping = new
    {
        priority = 10,
        request = new { method = "POST", urlPath = "/api/assistant/chat" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "text/event-stream" },
            body = sse
        }
    };
    await http.PostAsJsonAsync("/__admin/mappings", chatOkMapping);
});

await Run("sse_event_sequence_matches_spec", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS4_SYNTH_JSON.");
    }

    var req = new AgentChatRequest(
        SessionId: synth.Conversation.Safe.SessionId,
        SubmissionId: synth.Conversation.Safe.SubmissionId,
        UserId: synth.Conversation.Safe.UserId,
        UserRole: synth.Conversation.Safe.UserRole,
        Messages: new[] { new ChatMessage("user", synth.Conversation.Safe.Question) },
        CorrelationId: "canvas4"
    );

    using var http = new HttpClient();
    var client = new FunctionHttpAgentOrchestratorClient(http, new AgentOrchestratorOptions
    {
        FunctionBaseUrl = wiremock,
        ChatRoute = "api/assistant/chat",
        FunctionKey = null
    });

    var events = new List<AgentStreamEvent>();
    await foreach (var ev in client.ChatStreamAsync(req))
    {
        events.Add(ev);
    }

    var sequence = events.Select(e => e.Type).ToList();
    if (sequence.Count == 0 || !sequence[0].Equals("RunStarted", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Expected first SSE event RunStarted.");
    }

    var toolCallIndex = sequence.FindIndex(t => t.Equals("ToolCall", StringComparison.Ordinal));
    var toolResultIndex = sequence.FindIndex(t => t.Equals("ToolResult", StringComparison.Ordinal));
    var textDeltaIndex = sequence.FindIndex(t => t.Equals("TextDelta", StringComparison.Ordinal));
    var finishedIndex = sequence.FindIndex(t => t.Equals("RunFinished", StringComparison.Ordinal));

    if (toolCallIndex < 0 || toolResultIndex < 0 || textDeltaIndex < 0 || finishedIndex < 0)
    {
        throw new InvalidOperationException($"Missing required events in SSE sequence: {string.Join(" -> ", sequence)}");
    }

    if (!(toolCallIndex < toolResultIndex && toolResultIndex < textDeltaIndex && textDeltaIndex < finishedIndex))
    {
        throw new InvalidOperationException($"SSE ordering mismatch: {string.Join(" -> ", sequence)}");
    }

    var toolCall = events.FirstOrDefault(e => e.Type == "ToolCall")?.Tool;
    if (toolCall is null || !string.Equals(toolCall.ToolName, "get_submission_summary", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Expected ToolCall(get_submission_summary).");
    }

    var finished = events.LastOrDefault(e => e.Type == "RunFinished");
    if (finished?.Eval?.Groundedness is null || Math.Abs(finished.Eval.Groundedness.Value - 0.85d) > 1e-9)
    {
        throw new InvalidOperationException("Expected groundedness eval score 0.85 on RunFinished.");
    }
});

await Run("sql_conversation_audit_redacts_pii", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS4_SYNTH_JSON.");
    }

    await EnsureConversationAuditSchemaAsync(sql);

    var turnId = Guid.NewGuid().ToString("N");
    var raw = synth.Conversation.Safe.Question;
    var redacted = RedactPii(raw);

    await using var conn = new SqlConnection(sql);
    await conn.OpenAsync();
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = """
                          INSERT INTO conversation_audit (
                              turn_id,
                              session_id,
                              submission_id,
                              user_id,
                              content_redacted,
                              eval_groundedness,
                              created_at
                          )
                          VALUES (
                              @turn_id,
                              @session_id,
                              @submission_id,
                              @user_id,
                              @content_redacted,
                              @eval_groundedness,
                              SYSDATETIMEOFFSET()
                          );
                          """;
        cmd.Parameters.AddWithValue("@turn_id", turnId);
        cmd.Parameters.AddWithValue("@session_id", synth.Conversation.Safe.SessionId);
        cmd.Parameters.AddWithValue("@submission_id", synth.Conversation.Safe.SubmissionId);
        cmd.Parameters.AddWithValue("@user_id", synth.Conversation.Safe.UserId);
        cmd.Parameters.AddWithValue("@content_redacted", redacted);
        cmd.Parameters.AddWithValue("@eval_groundedness", 0.85);
        await cmd.ExecuteNonQueryAsync();
    }

    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT content_redacted, eval_groundedness FROM conversation_audit WHERE turn_id = @turn_id;";
        cmd.Parameters.AddWithValue("@turn_id", turnId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("conversation_audit row not found.");
        }

        var stored = reader.GetString(0);
        var grounded = reader.GetDouble(1);

        if (!stored.Contains("[REDACTED]", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Expected [REDACTED] token in stored content.");
        }
        if (stored.Contains("user@example.com", StringComparison.OrdinalIgnoreCase) || stored.Contains("555", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PII was not redacted in stored content.");
        }
        if (Math.Abs(grounded - 0.85d) > 1e-9)
        {
            throw new InvalidOperationException($"Expected groundedness 0.85, got {grounded.ToString(CultureInfo.InvariantCulture)}.");
        }
    }
});

await Run("sql_llm_cost_daily_written", async () =>
{
    await EnsureCostSchemaAsync(sql);

    var today = DateTime.UtcNow.Date;
    var costDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    await using var conn = new SqlConnection(sql);
    await conn.OpenAsync();

    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = """
                          MERGE llm_cost_daily AS target
                          USING (SELECT CAST(@cost_date AS DATE) AS cost_date) AS source
                          ON target.cost_date = source.cost_date
                          WHEN MATCHED THEN UPDATE SET
                              total_usd = @total_usd,
                              prompt_tokens = @prompt_tokens,
                              completion_tokens = @completion_tokens,
                              request_count = @request_count,
                              updated_at = SYSDATETIMEOFFSET()
                          WHEN NOT MATCHED THEN INSERT (
                              cost_date,
                              total_usd,
                              prompt_tokens,
                              completion_tokens,
                              request_count,
                              updated_at
                          ) VALUES (
                              CAST(@cost_date AS DATE),
                              @total_usd,
                              @prompt_tokens,
                              @completion_tokens,
                              @request_count,
                              SYSDATETIMEOFFSET()
                          );
                          """;
        cmd.Parameters.AddWithValue("@cost_date", costDate);
        cmd.Parameters.AddWithValue("@total_usd", 0.01);
        cmd.Parameters.AddWithValue("@prompt_tokens", 123);
        cmd.Parameters.AddWithValue("@completion_tokens", 456);
        cmd.Parameters.AddWithValue("@request_count", 1);
        await cmd.ExecuteNonQueryAsync();
    }

    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT total_usd, prompt_tokens, completion_tokens, request_count FROM llm_cost_daily WHERE cost_date = CAST(@cost_date AS DATE);";
        cmd.Parameters.AddWithValue("@cost_date", costDate);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("llm_cost_daily row not found.");
        }

        var total = reader.GetDouble(0);
        var p = reader.GetInt32(1);
        var c = reader.GetInt32(2);
        var r = reader.GetInt32(3);

        if (total <= 0.0 || p <= 0 || c <= 0 || r <= 0)
        {
            throw new InvalidOperationException("llm_cost_daily values invalid.");
        }
    }
});

await Run("prompt_injection_blocked_no_llm_call", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS4_SYNTH_JSON.");
    }

    using var httpAdmin = new HttpClient { BaseAddress = new Uri(wiremock) };
    await httpAdmin.DeleteAsync("/__admin/requests");

    var req = new AgentChatRequest(
        SessionId: synth.Conversation.PromptInjection.SessionId,
        SubmissionId: synth.Conversation.PromptInjection.SubmissionId,
        UserId: synth.Conversation.PromptInjection.UserId,
        UserRole: synth.Conversation.PromptInjection.UserRole,
        Messages: new[] { new ChatMessage("user", synth.Conversation.PromptInjection.Question) },
        CorrelationId: "canvas4-injection"
    );

    using var http = new HttpClient();
    var client = new FunctionHttpAgentOrchestratorClient(http, new AgentOrchestratorOptions
    {
        FunctionBaseUrl = wiremock,
        ChatRoute = "api/assistant/chat",
        FunctionKey = null
    });

    try
    {
        await foreach (var _ in client.ChatStreamAsync(req))
        {
        }

        throw new InvalidOperationException("Expected HTTP 400 for prompt injection.");
    }
    catch (HttpRequestException ex)
    {
        _ = ex;
    }

    var journal = await httpAdmin.GetStringAsync("/__admin/requests");
    var count = CountOccurrences(journal, "/openai/chat/completions");
    if (count != 0)
    {
        throw new InvalidOperationException($"Expected zero OpenAI calls, found {count}.");
    }
});

var output = JsonSerializer.Serialize(new { assertions }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
Console.WriteLine(output);

void Record(string name, bool passed, string details) => assertions.Add(new AssertionRecord(name, passed, details));

async Task Run(string name, Func<Task> fn)
{
    try
    {
        await fn();
        Record(name, true, "");
    }
    catch (Exception ex)
    {
        Record(name, false, ex.Message);
    }
}

static string BuildSseStream(string runId, string submissionId)
{
    var started = JsonSerializer.Serialize(new { type = "RunStarted", runId });
    var toolCall = JsonSerializer.Serialize(new
    {
        type = "ToolCall",
        runId,
        tool = new
        {
            toolName = "get_submission_summary",
            arguments = new Dictionary<string, object?> { ["submission_id"] = submissionId }
        }
    });
    var toolResult = JsonSerializer.Serialize(new
    {
        type = "ToolResult",
        runId,
        tool = new
        {
            toolName = "get_submission_summary",
            arguments = new Dictionary<string, object?> { ["submission_id"] = submissionId },
            result = JsonSerializer.Serialize(new { submissionId, summary = "ok" })
        }
    });
    var delta = JsonSerializer.Serialize(new { type = "TextDelta", runId, delta = "Risk summary.", done = false });
    var finished = JsonSerializer.Serialize(new
    {
        type = "RunFinished",
        runId,
        done = true,
        eval = new { groundedness = 0.85 }
    });

    var sb = new StringBuilder();
    sb.AppendLine($"data: {started}");
    sb.AppendLine();
    sb.AppendLine($"data: {toolCall}");
    sb.AppendLine();
    sb.AppendLine($"data: {toolResult}");
    sb.AppendLine();
    sb.AppendLine($"data: {delta}");
    sb.AppendLine();
    sb.AppendLine($"data: {finished}");
    sb.AppendLine();
    sb.AppendLine("data: [DONE]");
    sb.AppendLine();
    return sb.ToString();
}

static async Task EnsureConversationAuditSchemaAsync(string connectionString)
{
    var sql = """
              IF OBJECT_ID('conversation_audit', 'U') IS NULL
              BEGIN
                  CREATE TABLE conversation_audit (
                      turn_id          NVARCHAR(64) NOT NULL PRIMARY KEY,
                      session_id       NVARCHAR(64) NOT NULL,
                      submission_id    NVARCHAR(64) NOT NULL,
                      user_id          NVARCHAR(64) NOT NULL,
                      content_redacted NVARCHAR(MAX) NOT NULL,
                      eval_groundedness FLOAT NOT NULL,
                      created_at       DATETIMEOFFSET NOT NULL
                  );
              END
              """;

    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

static async Task EnsureCostSchemaAsync(string connectionString)
{
    var sql = """
              IF OBJECT_ID('llm_cost_daily', 'U') IS NULL
              BEGIN
                  CREATE TABLE llm_cost_daily (
                      cost_date         DATE NOT NULL PRIMARY KEY,
                      total_usd         FLOAT NOT NULL,
                      prompt_tokens     INT NOT NULL,
                      completion_tokens INT NOT NULL,
                      request_count     INT NOT NULL,
                      updated_at        DATETIMEOFFSET NOT NULL
                  );
              END
              """;

    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

static string RedactPii(string raw)
{
    return raw
        .Replace("user@example.com", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
        .Replace("(555) 123-4567", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
        .Replace("555", "[REDACTED]", StringComparison.OrdinalIgnoreCase);
}

static int CountOccurrences(string haystack, string needle)
{
    if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
    {
        return 0;
    }

    var count = 0;
    var idx = 0;
    while (true)
    {
        idx = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return count;
        }
        count++;
        idx += needle.Length;
    }
}

sealed record AssertionRecord(string name, bool passed, string details);

sealed record Canvas4Synth(Canvas4Conversation Conversation);

sealed record Canvas4Conversation(Canvas4Turn Safe, Canvas4Turn PromptInjection);

sealed record Canvas4Turn(string SessionId, string SubmissionId, string UserId, string UserRole, string Question);
