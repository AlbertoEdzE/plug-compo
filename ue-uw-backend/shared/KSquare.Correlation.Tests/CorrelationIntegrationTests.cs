using FluentAssertions;
using KSquare.Correlation.Extensions;
using KSquare.Correlation.Models;
using KSquare.Correlation.Tests.Synthesizers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.Correlation.Tests;

public sealed class CorrelationIntegrationTests
{
    [Fact]
    public async Task Preserves_correlation_id_through_middleware_and_http_handler()
    {
        var synthesizer = new CorrelationDataSynthesizer();
        var expectedCorrelationId = synthesizer.CorrelationId();

        using var downstreamServer = new TestServer(new WebHostBuilder().Configure(app =>
            app.Run(async context =>
            {
                var correlationId = context.Request.Headers[CorrelationHeaders.CorrelationId].ToString();
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync(correlationId, context.RequestAborted);
            })
        ));

        using var upstreamServer = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKsCorrelation();
                services.AddHttpClient("downstream")
                    .ConfigurePrimaryHttpMessageHandler(() => downstreamServer.CreateHandler())
                    .AddKsCorrelationPropagation();
            })
            .Configure(app =>
            {
                app.UseKsCorrelation();

                app.Run(async context =>
                {
                    var factory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
                    var client = factory.CreateClient("downstream");

                    var response = await client.GetAsync("http://downstream.test/", context.RequestAborted);
                    var body = await response.Content.ReadAsStringAsync(context.RequestAborted);

                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.WriteAsync(body, context.RequestAborted);
                });
            })
        );

        using var upstreamClient = upstreamServer.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://upstream.test/");
        request.Headers.TryAddWithoutValidation(CorrelationHeaders.CorrelationId, expectedCorrelationId);

        var upstreamResponse = await upstreamClient.SendAsync(request);
        var upstreamBody = await upstreamResponse.Content.ReadAsStringAsync();

        upstreamBody.Should().Be(expectedCorrelationId);
        upstreamResponse.Headers.Contains(CorrelationHeaders.CorrelationId).Should().BeTrue();
        upstreamResponse.Headers.GetValues(CorrelationHeaders.CorrelationId).Single().Should().Be(expectedCorrelationId);
    }
}
