namespace KSquare.PolicyAdminAdapter.Contracts;

using KSquare.PolicyAdminAdapter.Models;

public interface IPolicyAdminPayloadBuilder
{
    PolicyAdminPayload Build(BindRequest request);
}

