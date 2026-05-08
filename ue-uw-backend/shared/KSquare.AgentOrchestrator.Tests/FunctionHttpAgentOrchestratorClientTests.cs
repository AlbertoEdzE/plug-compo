using System.Net;
using System.Text;
using FluentAssertions;
using KSquare.AgentOrchestrator.Configuration;
using KSquare.AgentOrchestrator.Models;
using KSquare.AgentOrchestrator.Providers;

namespace KSquare.AgentOrchestrator.Tests;

public sealed class FunctionHttpAgentOrchestratorClientTests
{
    [Fact]
    public async Task ChatStreamAsync_parses_sse_events_until_done()
    {
        var sse = string.Join("\n", new[]
        {
            "data: {\"type\":\"RunStarted\",\"runId\":\"r1\"}",
            "",
            "data: {\"type\":\"TextDelta\",\"runId\":\"r1\",\"delta\":\"Hi\"}",
            "",
            "data: {\"type\":\"RunFinished\",\"runId\":\"r1\",\"done\":true}",
            "",
            "data: [DONE]",
            "",
        });

        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse)))
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return response;
        });

        var http = new HttpClient(handler);
        var client = new FunctionHttpAgentOrchestratorClient(http, new AgentOrchestratorOptions
        {
            FunctionBaseUrl = "http://localhost:7071/",
            FunctionKey = "key"
        });

        var req = new AgentChatRequest(
            SessionId: "s1",
            SubmissionId: "SUB-1",
            UserId: "u1",
            UserRole: "UNDERWRITER",
            Messages: [new ChatMessage("user", "hello")]
        );

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in client.ChatStreamAsync(req))
        {
            events.Add(ev);
        }

        events.Should().HaveCount(3);
        events[0].Type.Should().Be("RunStarted");
        events[1].Type.Should().Be("TextDelta");
        events[1].Delta.Should().Be("Hi");
        events[2].Type.Should().Be("RunFinished");
        events[2].Done.Should().BeTrue();
        handler.LastRequest!.RequestUri!.Query.Should().Contain("code=key");
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(handle(request));
        }
    }
}

