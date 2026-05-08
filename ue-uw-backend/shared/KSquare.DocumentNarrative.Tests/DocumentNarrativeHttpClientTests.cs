using System.Net;
using FluentAssertions;
using KSquare.DocumentNarrative.Models;
using KSquare.DocumentNarrative.Providers;

namespace KSquare.DocumentNarrative.Tests;

public sealed class DocumentNarrativeHttpClientTests
{
    [Fact]
    public async Task GenerateNarrativeAsync_posts_snake_case_request_and_parses_response()
    {
        var handler = new RecordingHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().EndWith("/narrative/generate");

            var responseBody = """
            {
              "submission_id": "s-1",
              "narrative_type": "RiskSummary",
              "narrative_text": "A concise risk summary.",
              "sections": { "full": "A concise risk summary." },
              "word_count": 4,
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

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://narrative.test/") };
        var client = new DocumentNarrativeHttpClient(http);

        var result = await client.GenerateNarrativeAsync(new NarrativeRequest
        {
            SubmissionId = "s-1",
            NarrativeType = NarrativeType.RiskSummary,
            SubmissionContext = new SubmissionContext
            {
                SubmissionId = "s-1",
                InstitutionName = "Acme School District",
                InstitutionType = "K-12 Public District",
                State = "CA",
                NaicsCode = "611110",
                TotalInsuredValue = 25_000_000,
                Enrollment = 4_250,
                FteEmployees = 350,
                EffectiveDate = "2026-09-01",
                ExpirationDate = "2027-09-01",
                CoverageLines =
                [
                    new Dictionary<string, object?> { ["product"] = "GL", ["limit"] = 5_000_000, ["premium"] = 42_000 }
                ],
                RiskIndicators = new Dictionary<string, object?> { ["financial_stability"] = "Stable" },
                AppetiteFitScore = 0.72,
                AppetiteClassification = "Borderline"
            },
            LossHistory = null,
            UnderwriterName = "Underwriter",
            AdditionalNotes = "Notes",
            CorrelationId = "c-1"
        });

        handler.LastRequestBody.Should().Contain("\"submission_id\"");
        handler.LastRequestBody.Should().Contain("\"narrative_type\"");
        handler.LastRequestBody.Should().Contain("RiskSummary");
        result.SubmissionId.Should().Be("s-1");
        result.NarrativeType.Should().Be(NarrativeType.RiskSummary);
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
