namespace KSquare.BlobStorage.Configuration;

public class BlobStorageOptions
{
    public BlobProvider Provider { get; set; } = BlobProvider.Azure;
    public string? ConnectionString { get; set; }
    public string? AccountName { get; set; }
    public string? LocalRootPath { get; set; }
    public TimeSpan DefaultSasExpiry { get; set; } = TimeSpan.FromHours(1);
    public long MaxUploadSizeBytes { get; set; } = 100 * 1024 * 1024;
}

public enum BlobProvider
{
    Azure,
    LocalFileSystem
}
