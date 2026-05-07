using System.Security.Claims;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KSquare.Correlation.Middleware;

public sealed class CorrelationMiddleware(
    RequestDelegate next,
    ICorrelationContextAccessor correlation,
    ILogger<CorrelationMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var previous = correlation.Current;

        var correlationId = context.Request.Headers.TryGetValue(CorrelationHeaders.CorrelationId, out var correlationHeader)
            && !string.IsNullOrWhiteSpace(correlationHeader)
                ? correlationHeader.ToString()
                : Guid.NewGuid().ToString();

        var tenantId = context.Request.Headers.TryGetValue(CorrelationHeaders.TenantId, out var tenantHeader)
            && !string.IsNullOrWhiteSpace(tenantHeader)
                ? tenantHeader.ToString()
                : null;

        var userId = context.User.FindFirstValue("sub");

        correlation.Current = new CorrelationContext(correlationId, tenantId, userId);
        context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"] = tenantId,
        });

        try
        {
            await next(context);
        }
        finally
        {
            correlation.Current = previous;
        }
    }
}
