namespace KSquare.PolicyAdminAdapter.Contracts;

using KSquare.PolicyAdminAdapter.Models;

public interface IBindReadinessValidator
{
    Task<BindReadinessResult> ValidateAsync(BindRequest request, CancellationToken ct = default);
}

