namespace KSquare.EventBus.Configuration;

public class EventBusOptions
{
    public EventBusProvider Provider { get; set; } = EventBusProvider.AzureServiceBus;
    public string? ConnectionString { get; set; }
    public bool UseOutbox { get; set; } = true;
    public TimeSpan OutboxPollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int OutboxMaxRetries { get; set; } = 5;
    public string OutboxConnectionString { get; set; } = "";
}

public enum EventBusProvider
{
    AzureServiceBus,
    InMemory
}
