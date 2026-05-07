using System.Security.Cryptography;
using System.Text.Json;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Exceptions;
using KSquare.BlobStorage.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KSquare.BlobStorage.Providers;

public sealed class LocalFileSystemConnector : IBlobStorageConnector
{
    private readonly BlobStorageOptions _options;
    private readonly ILogger<LocalFileSystemConnector> _logger;

    public LocalFileSystemConnector(IOptions<BlobStorageOptions> options, ILogger<LocalFileSystemConnector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
        {
            throw new InvalidOperationException("LocalRootPath must be set for LocalFileSystem provider.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var (tempFilePath, sizeBytes, md5Bytes) = await CopyToTempFileAsync(
            request.Content,
            _options.MaxUploadSizeBytes,
            ct
        );

        var destinationFilePath = ResolveFilePath(_options.LocalRootPath, request.ContainerName, request.BlobPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);

        if (File.Exists(destinationFilePath))
        {
            File.Delete(destinationFilePath);
        }

        File.Move(tempFilePath, destinationFilePath);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Metadata is not null)
        {
            foreach (var (key, value) in request.Metadata)
            {
                metadata[key] = value;
            }
        }

        var metadataPath = MetadataFilePath(destinationFilePath);
        await WriteMetadataAsync(metadataPath, request.ContentType, metadata, request.Tier, ct);

        var canonicalPath = CanonicalBlobPath(request.ContainerName, request.BlobPath);
        _logger.LogInformation(
            "Uploaded local blob {BlobPath} ({SizeBytes} bytes) in {DurationMs}ms",
            canonicalPath,
            sizeBytes,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
        );

        return new BlobUploadResult(
            canonicalPath,
            request.ContainerName,
            sizeBytes,
            Convert.ToHexString(md5Bytes).ToLowerInvariant(),
            DateTimeOffset.UtcNow
        );
    }

    public async Task<BlobDownloadResult> DownloadAsync(string blobPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
        {
            throw new InvalidOperationException("LocalRootPath must be set for LocalFileSystem provider.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);

        var filePath = ResolveFilePath(_options.LocalRootPath, containerName, relativeBlobPath);
        if (!File.Exists(filePath))
        {
            throw new BlobNotFoundException(blobPath);
        }

        var metadataPath = MetadataFilePath(filePath);
        var (contentType, metadata) = await ReadMetadataAsync(metadataPath, ct);

        var fileInfo = new FileInfo(filePath);
        var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        _logger.LogInformation(
            "Downloaded local blob {BlobPath} ({SizeBytes} bytes) in {DurationMs}ms",
            blobPath,
            fileInfo.Length,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
        );

        return new BlobDownloadResult(
            stream,
            contentType,
            fileInfo.Length,
            metadata,
            fileInfo.LastWriteTimeUtc
        );
    }

    public Task<BlobSasResult> GenerateSasUrlAsync(BlobSasRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
        {
            throw new InvalidOperationException("LocalRootPath must be set for LocalFileSystem provider.");
        }

        var filePath = ResolveFilePath(_options.LocalRootPath, request.ContainerName, request.BlobPath);
        if (!File.Exists(filePath))
        {
            throw new BlobNotFoundException(CanonicalBlobPath(request.ContainerName, request.BlobPath));
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(request.Expiry);
        var uriBuilder = new UriBuilder
        {
            Scheme = "file",
            Host = string.Empty,
            Path = filePath,
            Query = $"expiresAt={expiresAt.ToUnixTimeSeconds()}"
        };

        return Task.FromResult(new BlobSasResult(uriBuilder.Uri.ToString(), expiresAt));
    }

    public Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
        {
            throw new InvalidOperationException("LocalRootPath must be set for LocalFileSystem provider.");
        }

        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);
        var filePath = ResolveFilePath(_options.LocalRootPath, containerName, relativeBlobPath);
        return Task.FromResult(File.Exists(filePath));
    }

    public async Task ArchiveAsync(string blobPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
        {
            throw new InvalidOperationException("LocalRootPath must be set for LocalFileSystem provider.");
        }

        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);
        var filePath = ResolveFilePath(_options.LocalRootPath, containerName, relativeBlobPath);
        if (!File.Exists(filePath))
        {
            throw new BlobNotFoundException(blobPath);
        }

        var metadataPath = MetadataFilePath(filePath);
        var (contentType, metadata) = await ReadMetadataAsync(metadataPath, ct);
        await WriteMetadataAsync(metadataPath, contentType, metadata, BlobTier.Archive, ct);
    }

    public Task DeleteAsync(string blobPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
        {
            throw new InvalidOperationException("LocalRootPath must be set for LocalFileSystem provider.");
        }

        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);
        var filePath = ResolveFilePath(_options.LocalRootPath, containerName, relativeBlobPath);
        if (!File.Exists(filePath))
        {
            throw new BlobNotFoundException(blobPath);
        }

        File.Delete(filePath);
        var metadataPath = MetadataFilePath(filePath);
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<BlobListItem> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
        {
            throw new InvalidOperationException("LocalRootPath must be set for LocalFileSystem provider.");
        }

        var (containerName, relativePrefix) = ParseCanonicalBlobPath(prefix);
        var containerRoot = ResolveContainerRoot(_options.LocalRootPath, containerName);
        if (!Directory.Exists(containerRoot))
        {
            yield break;
        }

        foreach (var filePath in Directory.EnumerateFiles(containerRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            if (filePath.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(containerRoot, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');

            if (!relativePath.StartsWith(relativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            var metadataPath = MetadataFilePath(filePath);
            var (_, metadata) = await ReadMetadataAsync(metadataPath, ct);

            yield return new BlobListItem(
                CanonicalBlobPath(containerName, relativePath),
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                metadata
            );
        }
    }

    private static string CanonicalBlobPath(string containerName, string blobPath) => $"{containerName.Trim('/')}/{blobPath.TrimStart('/')}";

    private static (string ContainerName, string RelativeBlobPath) ParseCanonicalBlobPath(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
        {
            throw new ArgumentException("blobPath is required.", nameof(blobPath));
        }

        var firstSlash = blobPath.IndexOf('/');
        if (firstSlash <= 0 || firstSlash >= blobPath.Length - 1)
        {
            throw new ArgumentException("blobPath must be in the form 'containerName/relativePath'.", nameof(blobPath));
        }

        return (blobPath[..firstSlash], blobPath[(firstSlash + 1)..]);
    }

    private static string ResolveContainerRoot(string localRootPath, string containerName)
    {
        var root = Path.GetFullPath(localRootPath);
        var containerRoot = Path.GetFullPath(Path.Combine(root, containerName));

        if (!containerRoot.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid LocalRootPath/containerName combination.");
        }

        return containerRoot;
    }

    private static string ResolveFilePath(string localRootPath, string containerName, string blobPath)
    {
        var root = ResolveContainerRoot(localRootPath, containerName);
        var candidate = Path.GetFullPath(Path.Combine(root, blobPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid blobPath.");
        }

        return candidate;
    }

    private static string MetadataFilePath(string filePath) => $"{filePath}.meta.json";

    private static async Task WriteMetadataAsync(
        string metadataPath,
        string contentType,
        IDictionary<string, string> metadata,
        BlobTier tier,
        CancellationToken ct
    )
    {
        var payload = new LocalBlobMetadata(contentType, new Dictionary<string, string>(metadata), tier);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(payload), ct);
    }

    private static async Task<(string ContentType, IDictionary<string, string> Metadata)> ReadMetadataAsync(string metadataPath, CancellationToken ct)
    {
        if (!File.Exists(metadataPath))
        {
            return ("application/octet-stream", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var json = await File.ReadAllTextAsync(metadataPath, ct);
        var payload = JsonSerializer.Deserialize<LocalBlobMetadata>(json);

        return (
            payload?.ContentType ?? "application/octet-stream",
            payload?.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        );
    }

    private static async Task<(string TempFilePath, long SizeBytes, byte[] Md5Bytes)> CopyToTempFileAsync(
        Stream content,
        long maxUploadSizeBytes,
        CancellationToken ct
    )
    {
        var tempFilePath = Path.GetTempFileName();
        long sizeBytes = 0;
        var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        try
        {
            await using var tempStream = File.Open(tempFilePath, FileMode.Truncate, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 64];

            while (true)
            {
                var read = await content.ReadAsync(buffer, ct);
                if (read <= 0)
                {
                    break;
                }

                sizeBytes += read;
                if (sizeBytes > maxUploadSizeBytes)
                {
                    throw new BlobSizeExceededException(sizeBytes, maxUploadSizeBytes);
                }

                hasher.AppendData(buffer.AsSpan(0, read));
                await tempStream.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            await tempStream.FlushAsync(ct);
            return (tempFilePath, sizeBytes, hasher.GetHashAndReset());
        }
        catch
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    private sealed record LocalBlobMetadata(string ContentType, Dictionary<string, string> Metadata, BlobTier Tier);
}
