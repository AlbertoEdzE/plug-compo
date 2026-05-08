using System.Collections.Concurrent;
using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Models;

namespace KSquare.PolicyAdminAdapter.Providers.Mock;

public sealed class MockPolicyAdminAdapter : IPolicyAdminAdapter
{
    private readonly ConcurrentDictionary<string, BindJob> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public Task<BindReadinessResult> ValidateBindReadinessAsync(BindRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(new BindReadinessResult { IsReady = true });
    }

    public Task<BindJob> SubmitBindAsync(BindRequest request, CancellationToken ct = default)
    {
        _ = ct;
        var jobId = "bind-mock-" + Guid.NewGuid().ToString("N");
        var policyNumber = $"POL-MOCK-{request.QuoteId[..Math.Min(8, request.QuoteId.Length)]}-{DateTimeOffset.UtcNow:yyyy}";

        var job = new BindJob
        {
            BindJobId = jobId,
            QuoteId = request.QuoteId,
            SubmissionId = request.SubmissionId,
            Status = BindJobStatus.Bound,
            PolicyNumber = policyNumber,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };

        _jobs[jobId] = job;
        return Task.FromResult(job);
    }

    public Task<BindJob> GetBindStatusAsync(string bindJobId, CancellationToken ct = default)
    {
        _ = ct;
        if (_jobs.TryGetValue(bindJobId, out var job))
        {
            return Task.FromResult(job);
        }

        throw new KeyNotFoundException($"Bind job '{bindJobId}' was not found.");
    }
}

