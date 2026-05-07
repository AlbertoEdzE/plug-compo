namespace KSquare.BlobStorage.Models;

public record BlobDownloadResult(
    Stream Content,
    string ContentType,
    long SizeBytes,
    IDictionary<string, string> Metadata,
    DateTimeOffset LastModified
) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await Content.DisposeAsync();
}
