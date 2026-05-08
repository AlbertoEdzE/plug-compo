namespace KSquare.RatingAdapter.Configuration;

public class RatingAdapterOptions
{
    public RatingProvider Provider { get; set; } = RatingProvider.UeRatingEngine;
    public string? RatingEngineBaseUrl { get; set; }
    public string? RatingEngineApiKey { get; set; }
    public string? RatingEngineVersion { get; set; } = "v2";
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakerResetTimeout { get; set; } = TimeSpan.FromMinutes(1);
}

public enum RatingProvider
{
    UeRatingEngine,
    Mock
}

