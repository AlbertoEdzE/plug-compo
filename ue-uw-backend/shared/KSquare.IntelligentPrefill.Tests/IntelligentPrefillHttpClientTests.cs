using System.Net;
using FluentAssertions;
using KSquare.IntelligentPrefill.Models;
using KSquare.IntelligentPrefill.Providers;

namespace KSquare.IntelligentPrefill.Tests;

public sealed class IntelligentPrefillHttpClientTests
{
    [Fact]
    public async Task PrefillAsync_posts_snake_case_request_and_parses_response()
    {
        var handler = new RecordingHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().EndWith("/prefill/run");

            var responseBody = """
            {
              "document_id": "d-1",
              "field_results": [
                {
                  "canonical_field": "total_enrollment",
                  "value": "4250",
                  "confidence": 0.82,
                  "source_text": "Total student enrollment: 4,250",
                  "reasoning": "Found labeled value.",
                  "needs_review": false
                }
              ],
              "total_fields_requested": 1,
              "total_fields_filled": 1,
              "total_needs_review": 0,
              "model_version": "gpt-4o",
              "prompt_version": "v1",
              "latency_ms": 12,
              "correlation_id": "c-1"
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://prefill.test/") };
        var client = new IntelligentPrefillHttpClient(http);

        var result = await client.PrefillAsync(new PrefillRequest
        {
            DocumentId = "d-1",
            DocumentText = "Total student enrollment: 4,250",
            DocumentType = "ApplicationForm",
            UnmappedFields =
            [
                new UnmappedField
                {
                    CanonicalField = "total_enrollment",
                    DisplayLabel = "Total Enrollment",
                    ExpectedType = "integer",
                    Description = "Total number of enrolled students across all grades"
                }
            ],
            CorrelationId = "c-1"
        });

        handler.LastRequestBody.Should().Contain("\"document_id\"");
        handler.LastRequestBody.Should().Contain("\"unmapped_fields\"");
        handler.LastRequestBody.Should().Contain("\"canonical_field\"");

        result.DocumentId.Should().Be("d-1");
        result.FieldResults.Should().HaveCount(1);
        result.FieldResults[0].CanonicalField.Should().Be("total_enrollment");
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
