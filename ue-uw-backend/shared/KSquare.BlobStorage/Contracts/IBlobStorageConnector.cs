using KSquare.BlobStorage.Models;

namespace KSquare.BlobStorage.Contracts;

public interface IBlobStorageConnector
{
    Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken ct = default);
    Task<BlobDownloadResult> DownloadAsync(string blobPath, CancellationToken ct = default);
    Task<BlobSasResult> GenerateSasUrlAsync(BlobSasRequest request, CancellationToken ct = default);
    Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default);
    Task ArchiveAsync(string blobPath, CancellationToken ct = default);
    Task DeleteAsync(string blobPath, CancellationToken ct = default);
    IAsyncEnumerable<BlobListItem> ListAsync(string prefix, CancellationToken ct = default);
}
