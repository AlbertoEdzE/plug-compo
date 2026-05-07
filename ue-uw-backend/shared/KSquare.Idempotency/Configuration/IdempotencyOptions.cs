namespace KSquare.Idempotency.Configuration;

public class IdempotencyOptions
{
    public IdempotencyProvider Provider { get; set; } = IdempotencyProvider.SqlServer;
    public string? ConnectionString { get; set; }
    public string? RedisConnectionString { get; set; }
    public TimeSpan DefaultHttpKeyTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan DefaultConsumerKeyTtl { get; set; } = TimeSpan.FromDays(7);
}

public enum IdempotencyProvider
{
    SqlServer,
    Redis,
    InMemory
}
