namespace KSquare.PolicyAdminAdapter.Contracts;

using KSquare.PolicyAdminAdapter.Models;

public interface IPolicyAdminAdapter
{
    Task<BindReadinessResult> ValidateBindReadinessAsync(BindRequest request, CancellationToken ct = default);

    Task<BindJob> SubmitBindAsync(BindRequest request, CancellationToken ct = default);

    Task<BindJob> GetBindStatusAsync(string bindJobId, CancellationToken ct = default);
}

