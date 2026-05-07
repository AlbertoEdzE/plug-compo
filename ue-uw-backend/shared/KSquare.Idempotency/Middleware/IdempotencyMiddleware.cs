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
        if (cached is not null && cached.StatusCode > 0)
        {
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = cached.ContentType;
            await context.Response.WriteAsync(cached.ResponseBody, context.RequestAborted);
            return;
        }

        if (cached is not null && cached.StatusCode == 0)
        {
            var completed = await WaitForCompletionAsync(key, context.RequestAborted);
            if (completed is not null)
            {
                context.Response.StatusCode = completed.StatusCode;
                context.Response.ContentType = completed.ContentType;
                await context.Response.WriteAsync(completed.ResponseBody, context.RequestAborted);
                return;
            }
        }

        var reservation = new IdempotencyResult(0, string.Empty, "application/json", DateTimeOffset.UtcNow);
        await guard.SetAsync(key, reservation, ttl: null, context.RequestAborted);
        var reserved = await guard.GetAsync(key, context.RequestAborted);
        if (reserved is not null && reserved.StatusCode == 0 && reserved.ProcessedAt == reservation.ProcessedAt)
        {
            await ProcessAndCacheAsync(context, key);
            return;
        }

        var afterReservation = await WaitForCompletionAsync(key, context.RequestAborted);
        if (afterReservation is not null)
        {
            context.Response.StatusCode = afterReservation.StatusCode;
            context.Response.ContentType = afterReservation.ContentType;
            await context.Response.WriteAsync(afterReservation.ResponseBody, context.RequestAborted);
            return;
        }

        await ProcessAndCacheAsync(context, key);
    }

    private async Task ProcessAndCacheAsync(HttpContext context, string key)
    {
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
            else
            {
                var release = new IdempotencyResult(-1, string.Empty, "application/json", DateTimeOffset.UtcNow);
                await guard.SetAsync(key, release, ttl: TimeSpan.Zero, context.RequestAborted);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private async Task<IdempotencyResult?> WaitForCompletionAsync(string key, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);

        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            var current = await guard.GetAsync(key, ct);
            if (current is null)
            {
                return null;
            }

            if (current.StatusCode > 0)
            {
                return current;
            }

            await Task.Delay(50, ct);
        }

        return null;
    }
}
