namespace KSquare.DocumentExtraction.Configuration;

public class DocumentExtractionOptions
{
    public DocumentExtractionProvider Provider { get; set; } = DocumentExtractionProvider.AzureDocumentIntelligence;

    public string? FunctionBaseUrl { get; set; }

    public float LowConfidenceThreshold { get; set; } = 0.75f;
    public float AutoAcceptThreshold { get; set; } = 0.90f;

    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan SasExpiry { get; set; } = TimeSpan.FromHours(1);
}

public enum DocumentExtractionProvider
{
    AzureDocumentIntelligence,
    AwsTextract,
    Tesseract,
    Mock
}
