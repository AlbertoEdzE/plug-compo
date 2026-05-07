namespace KSquare.EventBus.Models;

public class EventPublishOptions
{
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? SessionId { get; set; }
    public TimeSpan? TimeToLive { get; set; }
    public IDictionary<string, string>? Properties { get; set; }
}
