using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Models;

namespace KSquare.PolicyAdminAdapter.Providers.Guidewire;

public sealed class GuidewirePayloadBuilder : IPolicyAdminPayloadBuilder
{
    public PolicyAdminPayload Build(BindRequest request)
    {
        _ = request;
        throw new NotSupportedException("Guidewire provider is a stub.");
    }
}

