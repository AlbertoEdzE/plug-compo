using System.Text;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Models;
using Microsoft.AspNetCore.Http;

namespace KSquare.Idempotency.Middleware;

public sealed class IdempotencyMiddleware(
    RequestDelegate next,
    IIdempotencyGuard guard,
    string headerName = "Idempotency-Key"
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var headerValues) || string.IsNullOrWhiteSpace(headerValues))
        {
            await next(context);
            return;
        }

        var key = headerValues.ToString();
        var cached = await guard.GetAsync(key, context.RequestAborted);
        if (cached is not null)
        {
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = cached.ContentType;
            await context.Response.WriteAsync(cached.ResponseBody, context.RequestAborted);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true)
                .ReadToEndAsync(context.RequestAborted);

            if (context.Response.StatusCode >= 200 && context.Response.StatusCode <= 299)
            {
                var contentType = context.Response.ContentType ?? "application/json";
                var result = new IdempotencyResult(context.Response.StatusCode, responseBody, contentType, DateTimeOffset.UtcNow);
                await guard.SetAsync(key, result, ttl: null, context.RequestAborted);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
