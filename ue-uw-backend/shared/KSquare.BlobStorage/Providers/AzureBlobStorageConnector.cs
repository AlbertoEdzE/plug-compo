using System.Security.Cryptography;
using Azure;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Exceptions;
using KSquare.BlobStorage.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KSquare.BlobStorage.Providers;

public sealed class AzureBlobStorageConnector : IBlobStorageConnector
{
    private readonly BlobStorageOptions _options;
    private readonly ILogger<AzureBlobStorageConnector> _logger;
    private readonly BlobServiceClient _serviceClient;
    private readonly string? _accountName;
    private readonly StorageSharedKeyCredential? _sharedKeyCredential;

    public AzureBlobStorageConnector(IOptions<BlobStorageOptions> options, ILogger<AzureBlobStorageConnector> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            _serviceClient = new BlobServiceClient(_options.ConnectionString);
            (_accountName, _sharedKeyCredential) = TryParseSharedKeyCredential(_options.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(_options.AccountName))
        {
            _accountName = _options.AccountName;
            _serviceClient = new BlobServiceClient(
                new Uri($"https://{_options.AccountName}.blob.core.windows.net"),
                new DefaultAzureCredential()
            );
        }
        else
        {
            throw new InvalidOperationException("Azure provider requires ConnectionString or AccountName.");
        }
    }

    public async Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var (tempFilePath, sizeBytes, md5Bytes) = await CopyToTempFileAsync(
            request.Content,
            _options.MaxUploadSizeBytes,
            ct
        );

        try
        {
            var containerClient = _serviceClient.GetBlobContainerClient(request.ContainerName);
            var blobClient = containerClient.GetBlobClient(request.BlobPath);

            await using var fileStream = File.OpenRead(tempFilePath);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = request.ContentType,
                    ContentHash = md5Bytes
                },
                Metadata = request.Metadata is null ? null : new Dictionary<string, string>(request.Metadata),
                AccessTier = request.Tier switch
                {
                    BlobTier.Cool => AccessTier.Cool,
                    BlobTier.Archive => AccessTier.Archive,
                    _ => AccessTier.Hot
                }
            };

            await blobClient.UploadAsync(fileStream, uploadOptions, ct);

            var canonicalPath = CanonicalBlobPath(request.ContainerName, request.BlobPath);
            _logger.LogInformation(
                "Uploaded azure blob {BlobPath} ({SizeBytes} bytes) in {DurationMs}ms",
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
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            throw new BlobAuthException("Azure Blob Storage authorization failure.", ex);
        }
        finally
        {
            TryDeleteFile(tempFilePath);
        }
    }

    public async Task<BlobDownloadResult> DownloadAsync(string blobPath, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);

        try
        {
            var containerClient = _serviceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(relativeBlobPath);

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            var stream = await blobClient.OpenReadAsync(cancellationToken: ct);

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in properties.Value.Metadata)
            {
                metadata[key] = value;
            }

            _logger.LogInformation(
                "Downloaded azure blob {BlobPath} ({SizeBytes} bytes) in {DurationMs}ms",
                blobPath,
                properties.Value.ContentLength,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
            );

            return new BlobDownloadResult(
                stream,
                properties.Value.ContentType ?? "application/octet-stream",
                properties.Value.ContentLength,
                metadata,
                properties.Value.LastModified
            );
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new BlobNotFoundException(blobPath);
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            throw new BlobAuthException("Azure Blob Storage authorization failure.", ex);
        }
    }

    public async Task<BlobSasResult> GenerateSasUrlAsync(BlobSasRequest request, CancellationToken ct = default)
    {
        var containerClient = _serviceClient.GetBlobContainerClient(request.ContainerName);
        var blobClient = containerClient.GetBlobClient(request.BlobPath);
        var canonicalPath = CanonicalBlobPath(request.ContainerName, request.BlobPath);

        try
        {
            if (!await blobClient.ExistsAsync(ct))
            {
                throw new BlobNotFoundException(canonicalPath);
            }

            var expiresAt = DateTimeOffset.UtcNow.Add(request.Expiry);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = request.ContainerName,
                BlobName = request.BlobPath,
                Resource = "b",
                ExpiresOn = expiresAt
            };

            sasBuilder.SetPermissions(request.Permissions switch
            {
                BlobSasPermissions.Read => Azure.Storage.Sas.BlobSasPermissions.Read,
                BlobSasPermissions.Write => Azure.Storage.Sas.BlobSasPermissions.Write,
                BlobSasPermissions.ReadWrite => Azure.Storage.Sas.BlobSasPermissions.Read | Azure.Storage.Sas.BlobSasPermissions.Write,
                BlobSasPermissions.Delete => Azure.Storage.Sas.BlobSasPermissions.Delete,
                _ => Azure.Storage.Sas.BlobSasPermissions.Read
            });

            if (!string.IsNullOrWhiteSpace(request.ContentDisposition))
            {
                sasBuilder.ContentDisposition = request.ContentDisposition;
            }

            var query = _sharedKeyCredential is not null
                ? sasBuilder.ToSasQueryParameters(_sharedKeyCredential).ToString()
                : await CreateUserDelegationSasQueryAsync(sasBuilder, expiresAt, ct);

            var sasUri = new UriBuilder(blobClient.Uri) { Query = query }.Uri;
            return new BlobSasResult(sasUri.ToString(), expiresAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new BlobNotFoundException(canonicalPath);
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            throw new BlobAuthException("Azure Blob Storage authorization failure.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default)
    {
        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);
        try
        {
            var containerClient = _serviceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(relativeBlobPath);
            return await blobClient.ExistsAsync(ct);
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            throw new BlobAuthException("Azure Blob Storage authorization failure.", ex);
        }
    }

    public async Task ArchiveAsync(string blobPath, CancellationToken ct = default)
    {
        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);

        try
        {
            var containerClient = _serviceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(relativeBlobPath);
            await blobClient.SetAccessTierAsync(AccessTier.Archive, cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new BlobNotFoundException(blobPath);
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            throw new BlobAuthException("Azure Blob Storage authorization failure.", ex);
        }
    }

    public async Task DeleteAsync(string blobPath, CancellationToken ct = default)
    {
        var (containerName, relativeBlobPath) = ParseCanonicalBlobPath(blobPath);

        try
        {
            var containerClient = _serviceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(relativeBlobPath);
            var deleted = await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            if (!deleted)
            {
                throw new BlobNotFoundException(blobPath);
            }
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            throw new BlobAuthException("Azure Blob Storage authorization failure.", ex);
        }
    }

    public async IAsyncEnumerable<BlobListItem> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (containerName, relativePrefix) = ParseCanonicalBlobPath(prefix);

        var containerClient = _serviceClient.GetBlobContainerClient(containerName);
        await foreach (var item in containerClient.GetBlobsAsync(traits: BlobTraits.Metadata, prefix: relativePrefix, cancellationToken: ct))
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.Metadata is not null)
            {
                foreach (var (key, value) in item.Metadata)
                {
                    metadata[key] = value;
                }
            }

            yield return new BlobListItem(
                CanonicalBlobPath(containerName, item.Name),
                item.Properties.ContentLength ?? 0,
                item.Properties.LastModified ?? DateTimeOffset.UtcNow,
                metadata
            );
        }
    }

    private async Task<string> CreateUserDelegationSasQueryAsync(BlobSasBuilder sasBuilder, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_accountName))
        {
            throw new InvalidOperationException("AccountName is required to generate SAS with user delegation.");
        }

        var now = DateTimeOffset.UtcNow;
        var delegationKey = await _serviceClient.GetUserDelegationKeyAsync(now.AddMinutes(-5), expiresAt, ct);
        return sasBuilder.ToSasQueryParameters(delegationKey.Value, _accountName).ToString();
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

    private static (string? AccountName, StorageSharedKeyCredential? Credential) TryParseSharedKeyCredential(string connectionString)
    {
        string? accountName = null;
        string? accountKey = null;

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase))
            {
                accountName = part["AccountName=".Length..];
            }
            else if (part.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase))
            {
                accountKey = part["AccountKey=".Length..];
            }
        }

        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(accountKey))
        {
            return (accountName, null);
        }

        return (accountName, new StorageSharedKeyCredential(accountName, accountKey));
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
            TryDeleteFile(tempFilePath);
            throw;
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }
}
