# Component 01 — Blob Storage Connector

**Library**: `KSquare.BlobStorage`  
**Layer**: Platform Infrastructure  
**Default Provider**: Azure Blob Storage  
**Alternate Providers**: AWS S3, local filesystem (test/dev)  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Blob Storage appears in **five places** across the UW workbench:
- Email Attachments Blob (email ingestion writes here)
- Submission Docs Blob (IDP writes processed docs here)
- Proposal Artifacts Blob (GhostDraft outputs stored here)
- Audit Archive Blob (future long-term audit retention)
- Analytics Export Blob (Power BI feed)

The complexity justifying a library:
- SAS URL generation with per-operation expiry and permission scoping
- Streaming upload/download to avoid memory pressure on large files
- Content-type detection and validation
- Lifecycle tag management (set retention class on upload)
- Provider abstraction so tests run against local filesystem without Azure
- Upload metadata propagation (correlationId, uploadedBy, documentType)

---

## Interface Contract

```csharp
namespace KSquare.BlobStorage.Contracts;

public interface IBlobStorageConnector
{
    // Upload a stream. Returns the canonical blob path.
    Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken ct = default);

    // Download a blob as a stream. Caller is responsible for disposing.
    Task<BlobDownloadResult> DownloadAsync(string blobPath, CancellationToken ct = default);

    // Generate a time-limited SAS URL for client-side download or upload.
    Task<BlobSasResult> GenerateSasUrlAsync(BlobSasRequest request, CancellationToken ct = default);

    // Check existence without downloading.
    Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default);

    // Soft-delete or move to archive tier.
    Task ArchiveAsync(string blobPath, CancellationToken ct = default);

    // Delete permanently.
    Task DeleteAsync(string blobPath, CancellationToken ct = default);

    // List blobs under a prefix.
    IAsyncEnumerable<BlobListItem> ListAsync(string prefix, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.BlobStorage.Models;

public record BlobUploadRequest(
    string ContainerName,
    string BlobPath,            // e.g. "incoming/2026/05/03/{correlationId}/file.pdf"
    Stream Content,
    string ContentType,
    IDictionary<string, string>? Metadata = null,  // correlationId, uploadedBy, documentType
    BlobTier Tier = BlobTier.Hot
);

public record BlobUploadResult(
    string BlobPath,
    string ContainerName,
    long SizeBytes,
    string ContentHash,         // MD5 for integrity
    DateTimeOffset UploadedAt
);

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

public record BlobSasRequest(
    string ContainerName,
    string BlobPath,
    BlobSasPermissions Permissions,    // Read, Write, Delete
    TimeSpan Expiry,
    string? ContentDisposition = null  // forces download filename
);

public record BlobSasResult(
    string SasUrl,
    DateTimeOffset ExpiresAt
);

public record BlobListItem(
    string BlobPath,
    long SizeBytes,
    DateTimeOffset LastModified,
    IDictionary<string, string> Metadata
);

public enum BlobTier { Hot, Cool, Archive }
public enum BlobSasPermissions { Read, Write, ReadWrite, Delete }
```

---

## Configuration

```csharp
namespace KSquare.BlobStorage.Configuration;

public class BlobStorageOptions
{
    public BlobProvider Provider { get; set; } = BlobProvider.Azure;

    // Azure Blob
    public string? ConnectionString { get; set; }   // from Key Vault
    public string? AccountName { get; set; }         // for Managed Identity auth

    // Local filesystem (dev/test)
    public string? LocalRootPath { get; set; }

    // Defaults
    public TimeSpan DefaultSasExpiry { get; set; } = TimeSpan.FromHours(1);
    public long MaxUploadSizeBytes { get; set; } = 100 * 1024 * 1024; // 100 MB
}

public enum BlobProvider { Azure, LocalFileSystem }
```

```json
// appsettings.json
{
  "KSquare": {
    "BlobStorage": {
      "Provider": "Azure",
      "AccountName": "stueueuwprod",
      "DefaultSasExpiry": "01:00:00",
      "MaxUploadSizeBytes": 104857600
    }
  }
}
```

Key Vault secret: `BlobStorage--ConnectionString`

---

## DI Registration

```csharp
// Program.cs in any consuming service
builder.Services.AddKsBlobStorage(options =>
{
    builder.Configuration.GetSection("KSquare:BlobStorage").Bind(options);
    options.ConnectionString = builder.Configuration["BlobStorage--ConnectionString"];
});
```

---

## Usage Examples

```csharp
// Upload email attachment
var result = await blob.UploadAsync(new BlobUploadRequest(
    ContainerName: "email-attachments",
    BlobPath: $"incoming/{DateTime.UtcNow:yyyy/MM/dd}/{correlationId}/{fileName}",
    Content: attachmentStream,
    ContentType: "application/pdf",
    Metadata: new Dictionary<string, string>
    {
        ["correlationId"] = correlationId,
        ["uploadedBy"] = "email-ingestion",
        ["documentType"] = "raw-attachment"
    }
));

// Generate SAS download URL for frontend
var sas = await blob.GenerateSasUrlAsync(new BlobSasRequest(
    ContainerName: "submission-docs",
    BlobPath: document.BlobPath,
    Permissions: BlobSasPermissions.Read,
    Expiry: TimeSpan.FromHours(1),
    ContentDisposition: $"attachment; filename=\"{document.FileName}\""
));
return sas.SasUrl;

// Stream download without loading into memory
await using var download = await blob.DownloadAsync(blobPath);
await download.Content.CopyToAsync(responseStream);
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Blob not found | Throws `BlobNotFoundException(blobPath)` |
| Upload too large | Throws `BlobSizeExceededException(sizeBytes, maxBytes)` before upload begins |
| Network timeout | Retried 3× with exponential backoff (built into Azure SDK) |
| Auth failure | Throws `BlobAuthException` — do not retry, alert operator |
| SAS generation on non-existent blob | Throws `BlobNotFoundException` |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.BlobStorage at path: shared/KSquare.BlobStorage/

This is a provider-agnostic blob storage abstraction library. It must not contain any 
domain logic — it is pure infrastructure.

Project structure:
  shared/KSquare.BlobStorage/
  ├── KSquare.BlobStorage.csproj
  ├── Contracts/
  │   └── IBlobStorageConnector.cs
  ├── Models/
  │   ├── BlobUploadRequest.cs
  │   ├── BlobUploadResult.cs
  │   ├── BlobDownloadResult.cs
  │   ├── BlobSasRequest.cs
  │   ├── BlobSasResult.cs
  │   └── BlobListItem.cs
  ├── Configuration/
  │   └── BlobStorageOptions.cs
  ├── Providers/
  │   ├── AzureBlobStorageConnector.cs   ← Azure.Storage.Blobs implementation
  │   └── LocalFileSystemConnector.cs    ← local dev/test implementation
  ├── Exceptions/
  │   ├── BlobNotFoundException.cs
  │   ├── BlobSizeExceededException.cs
  │   └── BlobAuthException.cs
  └── Extensions/
      └── ServiceCollectionExtensions.cs ← AddKsBlobStorage(...)

Interface: IBlobStorageConnector (exact signatures from the spec above)
Models: exact record definitions from the spec above

AzureBlobStorageConnector implementation:
  - Use Azure.Storage.Blobs SDK
  - Support both ConnectionString and Managed Identity (DefaultAzureCredential)
  - UploadAsync: use BlobClient.UploadAsync with BlobUploadOptions (metadata, tags, tier)
  - DownloadAsync: use BlobClient.OpenReadAsync for streaming (no buffer full file)
  - GenerateSasUrlAsync: use BlobSasBuilder, sign with StorageSharedKeyCredential or user-delegation key
  - ExistsAsync: BlobClient.ExistsAsync
  - ListAsync: BlobContainerClient.GetBlobsAsync as IAsyncEnumerable
  - ArchiveAsync: SetAccessTierAsync(AccessTier.Archive)
  - Max upload size check before upload (throw BlobSizeExceededException if exceeded)
  - Set metadata on all uploads
  - Logging: ILogger<AzureBlobStorageConnector> for all operations with blob path, size, duration

LocalFileSystemConnector implementation:
  - Stores files under LocalRootPath/{containerName}/{blobPath}
  - Creates directories as needed
  - SAS URL: returns a file:// URI (for testing only)
  - Implements all interface methods

ServiceCollectionExtensions:
  public static IServiceCollection AddKsBlobStorage(
      this IServiceCollection services,
      Action<BlobStorageOptions> configure)
  - Reads Provider from options
  - Registers AzureBlobStorageConnector or LocalFileSystemConnector
  - Registers IBlobStorageConnector as singleton
  - Validates options (throws if Azure selected but no ConnectionString or AccountName)

NuGet packages:
  - Azure.Storage.Blobs 12.x
  - Azure.Identity 1.x
  - Microsoft.Extensions.DependencyInjection.Abstractions
  - Microsoft.Extensions.Logging.Abstractions
  - Microsoft.Extensions.Options

Tests at shared/KSquare.BlobStorage.Tests/:
  - Upload → Download roundtrip (LocalFileSystem provider)
  - Upload → ExistsAsync returns true
  - Download non-existent → BlobNotFoundException
  - Upload exceeds max size → BlobSizeExceededException before upload
  - SAS URL contains correct expiry
  - Metadata round-trips through upload/download
  - ListAsync returns correct items under prefix
  Use xUnit + FluentAssertions. No Azure credentials required in tests (use LocalFileSystem).
```
