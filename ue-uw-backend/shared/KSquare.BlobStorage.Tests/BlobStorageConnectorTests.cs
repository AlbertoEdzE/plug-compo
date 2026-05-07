using System.Text;
using FluentAssertions;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Exceptions;
using KSquare.BlobStorage.Extensions;
using KSquare.BlobStorage.Models;
using KSquare.BlobStorage.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.BlobStorage.Tests;

public sealed class BlobStorageConnectorTests
{
    [Fact]
    public async Task Upload_then_download_roundtrip_works()
    {
        var synthesizer = new BlobStorageDataSynthesizer();
        var rootPath = synthesizer.TempRootPath();

        try
        {
            var connector = CreateConnector(rootPath);

            var container = synthesizer.ContainerName();
            var blobPath = synthesizer.BlobPath("roundtrip.pdf");
            var contentType = synthesizer.ContentTypePdf();
            var metadata = synthesizer.Metadata();

            var bytes = Encoding.UTF8.GetBytes("hello world");
            await using var content = new MemoryStream(bytes);

            var upload = await connector.UploadAsync(new BlobUploadRequest(
                container,
                blobPath,
                content,
                contentType,
                metadata
            ));

            await using var download = await connector.DownloadAsync(upload.BlobPath);
            var downloadedBytes = await ReadAllBytesAsync(download.Content);

            downloadedBytes.Should().BeEquivalentTo(bytes);
            download.ContentType.Should().Be(contentType);
            download.Metadata.Should().ContainKeys(metadata.Keys);
            foreach (var (key, value) in metadata)
            {
                download.Metadata[key].Should().Be(value);
            }
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task Upload_then_exists_returns_true()
    {
        var synthesizer = new BlobStorageDataSynthesizer();
        var rootPath = synthesizer.TempRootPath();

        try
        {
            var connector = CreateConnector(rootPath);

            var container = synthesizer.ContainerName();
            var blobPath = synthesizer.BlobPath("exists.pdf");

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("content"));
            var upload = await connector.UploadAsync(new BlobUploadRequest(
                container,
                blobPath,
                content,
                synthesizer.ContentTypePdf()
            ));

            var exists = await connector.ExistsAsync(upload.BlobPath);
            exists.Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task Download_non_existent_blob_throws()
    {
        var synthesizer = new BlobStorageDataSynthesizer();
        var rootPath = synthesizer.TempRootPath();

        try
        {
            var connector = CreateConnector(rootPath);
            var container = synthesizer.ContainerName();

            var canonicalPath = $"{container}/missing/file.pdf";
            var act = async () => await connector.DownloadAsync(canonicalPath);

            await act.Should().ThrowAsync<BlobNotFoundException>();
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task Upload_exceeds_max_size_throws_before_upload()
    {
        var synthesizer = new BlobStorageDataSynthesizer();
        var rootPath = synthesizer.TempRootPath();

        try
        {
            var connector = CreateConnector(rootPath, maxUploadSizeBytes: 10);

            var container = synthesizer.ContainerName();
            var blobPath = synthesizer.BlobPath("too-big.pdf");

            await using var content = new MemoryStream(new byte[11]);
            var act = async () => await connector.UploadAsync(new BlobUploadRequest(
                container,
                blobPath,
                content,
                synthesizer.ContentTypePdf()
            ));

            await act.Should().ThrowAsync<BlobSizeExceededException>();

            var canonicalPath = $"{container}/{blobPath}";
            (await connector.ExistsAsync(canonicalPath)).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task Sas_url_contains_correct_expiry()
    {
        var synthesizer = new BlobStorageDataSynthesizer();
        var rootPath = synthesizer.TempRootPath();

        try
        {
            var connector = CreateConnector(rootPath);

            var container = synthesizer.ContainerName();
            var blobPath = synthesizer.BlobPath("sas.pdf");

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("content"));
            await connector.UploadAsync(new BlobUploadRequest(
                container,
                blobPath,
                content,
                synthesizer.ContentTypePdf()
            ));

            var expiry = TimeSpan.FromMinutes(5);
            var sas = await connector.GenerateSasUrlAsync(new BlobSasRequest(
                container,
                blobPath,
                BlobSasPermissions.Read,
                expiry
            ));

            var uri = new Uri(sas.SasUrl);
            var expiresAtFromUrl = ParseExpiresAtUnixSeconds(uri.Query);
            expiresAtFromUrl.Should().Be(sas.ExpiresAt.ToUnixTimeSeconds());
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task Metadata_roundtrips_through_upload_download()
    {
        var synthesizer = new BlobStorageDataSynthesizer();
        var rootPath = synthesizer.TempRootPath();

        try
        {
            var connector = CreateConnector(rootPath);

            var container = synthesizer.ContainerName();
            var blobPath = synthesizer.BlobPath("meta.pdf");
            var metadata = synthesizer.Metadata();

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("content"));
            var upload = await connector.UploadAsync(new BlobUploadRequest(
                container,
                blobPath,
                content,
                synthesizer.ContentTypePdf(),
                metadata
            ));

            await using var download = await connector.DownloadAsync(upload.BlobPath);
            download.Metadata.Should().ContainKeys(metadata.Keys);
            foreach (var (key, value) in metadata)
            {
                download.Metadata[key].Should().Be(value);
            }
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Fact]
    public async Task ListAsync_returns_items_under_prefix()
    {
        var synthesizer = new BlobStorageDataSynthesizer();
        var rootPath = synthesizer.TempRootPath();

        try
        {
            var connector = CreateConnector(rootPath);
            var container = synthesizer.ContainerName();

            await UploadTextAsync(connector, container, "incoming/2026/05/03/a.txt", "a");
            await UploadTextAsync(connector, container, "incoming/2026/05/03/b.txt", "b");
            await UploadTextAsync(connector, container, "incoming/2025/01/01/c.txt", "c");

            var items = new List<BlobListItem>();
            await foreach (var item in connector.ListAsync($"{container}/incoming/2026"))
            {
                items.Add(item);
            }

            items.Should().HaveCount(2);
            items.Select(i => i.BlobPath).Should().OnlyContain(p => p.StartsWith($"{container}/incoming/2026", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    private static IBlobStorageConnector CreateConnector(string localRootPath, long? maxUploadSizeBytes = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddKsBlobStorage(options =>
        {
            options.Provider = BlobProvider.LocalFileSystem;
            options.LocalRootPath = localRootPath;
            if (maxUploadSizeBytes is not null)
            {
                options.MaxUploadSizeBytes = maxUploadSizeBytes.Value;
            }
        });

        return services.BuildServiceProvider().GetRequiredService<IBlobStorageConnector>();
    }

    private static async Task UploadTextAsync(IBlobStorageConnector connector, string container, string blobPath, string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await connector.UploadAsync(new BlobUploadRequest(
            container,
            blobPath,
            stream,
            "text/plain",
            new Dictionary<string, string> { ["correlationId"] = Guid.NewGuid().ToString("N") }
        ));
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream content)
    {
        await using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static long ParseExpiresAtUnixSeconds(string query)
    {
        var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
            {
                continue;
            }

            if (kv[0].Equals("expiresAt", StringComparison.OrdinalIgnoreCase))
            {
                return long.Parse(Uri.UnescapeDataString(kv[1]));
            }
        }

        throw new InvalidOperationException("expiresAt query parameter not found.");
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
        }
    }
}
