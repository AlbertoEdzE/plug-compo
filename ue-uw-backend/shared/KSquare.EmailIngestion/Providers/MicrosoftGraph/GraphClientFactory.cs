using Azure.Identity;
using Microsoft.Graph;
using KSquare.EmailIngestion.Configuration;

namespace KSquare.EmailIngestion.Providers.MicrosoftGraph;

internal static class GraphClientFactory
{
    public static GraphServiceClient Create(EmailIngestionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TenantId)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException("TenantId, ClientId, and ClientSecret are required for MicrosoftGraph provider.");
        }

        var credential = new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        return new GraphServiceClient(credential, scopes);
    }
}
