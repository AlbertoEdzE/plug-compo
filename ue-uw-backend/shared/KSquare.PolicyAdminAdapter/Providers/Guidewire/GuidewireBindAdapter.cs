using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Models;

namespace KSquare.PolicyAdminAdapter.Providers.Guidewire;

public sealed class GuidewireBindAdapter : IPolicyAdminAdapter
{
    public Task<BindReadinessResult> ValidateBindReadinessAsync(BindRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        throw new NotSupportedException("Guidewire provider is a stub.");
    }

    public Task<BindJob> SubmitBindAsync(BindRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        throw new NotSupportedException("Guidewire provider is a stub.");
    }

    public Task<BindJob> GetBindStatusAsync(string bindJobId, CancellationToken ct = default)
    {
        _ = bindJobId;
        _ = ct;
        throw new NotSupportedException("Guidewire provider is a stub.");
    }
}

