using Azure.Messaging.ServiceBus;
using KSquare.EventBus.Configuration;

namespace KSquare.EventBus.Providers;

public sealed class AzureServiceBusProvider : IAsyncDisposable
{
    public AzureServiceBusProvider(EventBusOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for AzureServiceBus provider.");
        }

        Client = new ServiceBusClient(options.ConnectionString);
    }

    public ServiceBusClient Client { get; }

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
