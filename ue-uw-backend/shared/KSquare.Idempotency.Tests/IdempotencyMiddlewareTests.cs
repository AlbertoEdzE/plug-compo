using System.Net;
using System.Text;
using FluentAssertions;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.Idempotency.Tests;

public sealed class IdempotencyMiddlewareTests
{
    [Fact]
    public async Task Replays_cached_response_on_duplicate_key()
    {
        var processedCount = 0;

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKsIdempotency(options => options.Provider = IdempotencyProvider.InMemory);
            })
            .Configure(app =>
            {
                app.UseKsIdempotency();
                app.Run(async ctx =>
                {
                    Interlocked.Increment(ref processedCount);
                    ctx.Response.StatusCode = StatusCodes.Status201Created;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"ok\":true}", ctx.RequestAborted);
                });
            })
        );

        using var client = server.CreateClient();
        using var request1 = new HttpRequestMessage(HttpMethod.Post, "http://test.local/");
        request1.Headers.TryAddWithoutValidation("Idempotency-Key", "same-key");

        var response1 = await client.SendAsync(request1);
        var body1 = await response1.Content.ReadAsStringAsync();

        using var request2 = new HttpRequestMessage(HttpMethod.Post, "http://test.local/");
        request2.Headers.TryAddWithoutValidation("Idempotency-Key", "same-key");

        var response2 = await client.SendAsync(request2);
        var body2 = await response2.Content.ReadAsStringAsync();

        processedCount.Should().Be(1);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);
        body2.Should().Be(body1);
    }

    [Fact]
    public async Task Passes_through_when_header_missing()
    {
        var processedCount = 0;

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKsIdempotency(options => options.Provider = IdempotencyProvider.InMemory);
            })
            .Configure(app =>
            {
                app.UseKsIdempotency();
                app.Run(async ctx =>
                {
                    Interlocked.Increment(ref processedCount);
                    ctx.Response.StatusCode = StatusCodes.Status200OK;
                    ctx.Response.ContentType = "text/plain";
                    await ctx.Response.WriteAsync("ok", Encoding.UTF8, ctx.RequestAborted);
                });
            })
        );

        using var client = server.CreateClient();
        var response1 = await client.GetAsync("http://test.local/");
        var response2 = await client.GetAsync("http://test.local/");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        processedCount.Should().Be(2);
    }
}

