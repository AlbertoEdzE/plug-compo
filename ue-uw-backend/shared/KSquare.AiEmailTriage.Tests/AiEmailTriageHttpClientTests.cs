using System.Net;
using FluentAssertions;
using KSquare.AiEmailTriage.Models;
using KSquare.AiEmailTriage.Providers;

namespace KSquare.AiEmailTriage.Tests;

public sealed class AiEmailTriageHttpClientTests
{
    [Fact]
    public async Task TriageAsync_posts_snake_case_request_and_parses_response()
    {
        var handler = new RecordingHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().EndWith("/email/triage");

            var responseBody = """
            {
              "email_id": "e-1",
              "intent": "Other",
              "intent_confidence": 0.0,
              "extracted_entities": [],
              "routing_suggestion": "Manual",
              "urgency": "Normal",
              "urgency_signals": [],
              "summary": "Email from broker@example.com — Other.",
              "model_version": "mock",
              "prompt_version": "v1",
              "latency_ms": 10,
              "correlation_id": "c-1"
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://triage.test/") };
        var client = new AiEmailTriageHttpClient(http);

        var result = await client.TriageAsync(new EmailTriageRequest
        {
            EmailId = "e-1",
            Subject = "Hello",
            BodyText = "Body",
            SenderEmail = "broker@example.com",
            SenderName = "Broker",
            ReceivedAt = "2026-05-08T00:00:00Z",
            AttachmentNames = new[] { "acord.pdf" },
            CorrelationId = "c-1"
        });

        handler.LastRequestBody.Should().Contain("\"email_id\"");
        handler.LastRequestBody.Should().Contain("\"body_text\"");
        result.EmailId.Should().Be("e-1");
        result.RoutingSuggestion.Should().Be("Manual");
        result.CorrelationId.Should().Be("c-1");
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
