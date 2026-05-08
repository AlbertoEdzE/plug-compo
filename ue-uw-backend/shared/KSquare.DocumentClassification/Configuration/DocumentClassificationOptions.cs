namespace KSquare.DocumentClassification.Configuration;

public class DocumentClassificationOptions
{
    public DocumentClassificationProvider Provider { get; set; } = DocumentClassificationProvider.AzureThenHeuristic;
    public string? FunctionBaseUrl { get; set; }
    public string? AzureEndpoint { get; set; }
    public string? AzureClassifierModelId { get; set; } = "ksquare-doc-classifier-v1";
    public bool UseAzureManagedIdentity { get; set; } = true;
    public float AutoAcceptThreshold { get; set; } = 0.85f;
    public float ReviewThreshold { get; set; } = 0.70f;
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan SasExpiry { get; set; } = TimeSpan.FromHours(1);
}

public enum DocumentClassificationProvider
{
    AzureThenHeuristic,
    AzureOnly,
    HeuristicOnly,
    GptVision,
    Mock
}
