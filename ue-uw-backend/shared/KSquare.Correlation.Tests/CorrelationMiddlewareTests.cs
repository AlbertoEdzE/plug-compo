using System.Security.Claims;
using FluentAssertions;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Middleware;
using KSquare.Correlation.Models;
using KSquare.Correlation.Tests.Synthesizers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSquare.Correlation.Tests;

public sealed class CorrelationMiddlewareTests
{
    [Fact]
    public async Task Generates_new_correlation_id_when_header_missing()
    {
        var accessor = new CorrelationContextAccessor();
        var observedCorrelationId = default(string);

        RequestDelegate next = _ =>
        {
            observedCorrelationId = accessor.Current?.CorrelationId;
            return Task.CompletedTask;
        };

        var middleware = new CorrelationMiddleware(next, accessor, NullLogger<CorrelationMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();

        await middleware.InvokeAsync(httpContext);

        observedCorrelationId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(observedCorrelationId, out _).Should().BeTrue();

        httpContext.Response.Headers.ContainsKey(CorrelationHeaders.CorrelationId).Should().BeTrue();
        httpContext.Response.Headers[CorrelationHeaders.CorrelationId].ToString().Should().Be(observedCorrelationId);
    }

    [Fact]
    public async Task Uses_incoming_correlation_id_when_header_present()
    {
        var synthesizer = new CorrelationDataSynthesizer();
        var accessor = new CorrelationContextAccessor();

        var expectedCorrelationId = synthesizer.CorrelationId();
        var expectedTenantId = synthesizer.TenantId();

        var observed = default(ICorrelationContext);

        RequestDelegate next = _ =>
        {
            observed = accessor.Current;
            return Task.CompletedTask;
        };

        var middleware = new CorrelationMiddleware(next, accessor, NullLogger<CorrelationMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CorrelationHeaders.CorrelationId] = expectedCorrelationId;
        httpContext.Request.Headers[CorrelationHeaders.TenantId] = expectedTenantId;

        await middleware.InvokeAsync(httpContext);

        observed.Should().NotBeNull();
        observed!.CorrelationId.Should().Be(expectedCorrelationId);
        observed!.TenantId.Should().Be(expectedTenantId);

        httpContext.Response.Headers[CorrelationHeaders.CorrelationId].ToString().Should().Be(expectedCorrelationId);
    }

    [Fact]
    public async Task Extracts_user_id_from_sub_claim_when_present()
    {
        var synthesizer = new CorrelationDataSynthesizer();
        var accessor = new CorrelationContextAccessor();
        var expectedUserId = synthesizer.UserId();

        var observedUserId = default(string);

        RequestDelegate next = _ =>
        {
            observedUserId = accessor.Current?.UserId;
            return Task.CompletedTask;
        };

        var middleware = new CorrelationMiddleware(next, accessor, NullLogger<CorrelationMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();

        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", expectedUserId) },
            authenticationType: "test"
        ));

        await middleware.InvokeAsync(httpContext);

        observedUserId.Should().Be(expectedUserId);
    }
}
