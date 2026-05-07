using KSquare.Correlation.Contracts;
using KSquare.Correlation.Models;

namespace KSquare.Correlation.Http;

public sealed class CorrelationPropagationHandler(ICorrelationContextAccessor correlation) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var current = correlation.Current;
        if (current is not null)
        {
            if (!request.Headers.Contains(CorrelationHeaders.CorrelationId))
            {
                request.Headers.TryAddWithoutValidation(CorrelationHeaders.CorrelationId, current.CorrelationId);
            }

            if (!string.IsNullOrWhiteSpace(current.TenantId) && !request.Headers.Contains(CorrelationHeaders.TenantId))
            {
                request.Headers.TryAddWithoutValidation(CorrelationHeaders.TenantId, current.TenantId);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
