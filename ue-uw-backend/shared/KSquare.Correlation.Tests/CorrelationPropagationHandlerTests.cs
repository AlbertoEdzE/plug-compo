using System.Net;
using FluentAssertions;
using KSquare.Correlation.Http;
using KSquare.Correlation.Models;
using KSquare.Correlation.Tests.Synthesizers;

namespace KSquare.Correlation.Tests;

public sealed class CorrelationPropagationHandlerTests
{
    [Fact]
    public async Task Adds_correlation_headers_to_outgoing_request()
    {
        var synthesizer = new CorrelationDataSynthesizer();
        var accessor = new CorrelationContextAccessor();
        var correlationContext = synthesizer.Context();

        accessor.Current = correlationContext;

        var recorder = new RecordingHandler();
        var handler = new CorrelationPropagationHandler(accessor)
        {
            InnerHandler = recorder
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/");

        await client.SendAsync(request);

        recorder.LastRequest.Should().NotBeNull();
        recorder.LastRequest!.Headers.Contains(CorrelationHeaders.CorrelationId).Should().BeTrue();
        recorder.LastRequest!.Headers.GetValues(CorrelationHeaders.CorrelationId).Single().Should().Be(correlationContext.CorrelationId);

        recorder.LastRequest!.Headers.Contains(CorrelationHeaders.TenantId).Should().BeTrue();
        recorder.LastRequest!.Headers.GetValues(CorrelationHeaders.TenantId).Single().Should().Be(correlationContext.TenantId);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
