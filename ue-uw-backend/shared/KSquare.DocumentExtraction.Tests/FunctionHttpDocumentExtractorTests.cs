using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.DocumentExtraction.Configuration;
using KSquare.DocumentExtraction.Models;
using KSquare.DocumentExtraction.Providers;

namespace KSquare.DocumentExtraction.Tests;

public sealed class FunctionHttpDocumentExtractorTests
{
    [Fact]
    public async Task ExtractAsync_WithBlobPath_ShouldSendSasUrl_ToFunction()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new
            {
                documentId = "doc-1",
                providerOperationId = "op-1",
                status = "Succeeded",
                fields = new[]
                {
                    new
                    {
                        name = "insured_name",
                        value = "Acme Inc",
                        confidence = 0.95f,
                        boundingBox = (object?)null,
                        pageNumber = 1
                    }
                },
                tables = Array.Empty<object>(),
                pages = new[] { new { pageNumber = 1, width = 1000, height = 1400, unit = "pixel" } },
                detectedDocumentType = "ACORD125",
                overallConfidence = 0.95f,
                extractedAt = DateTimeOffset.UtcNow,
                modelUsed = "prebuilt-document",
                correlationId = (string?)null
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var blob = new FakeBlobStorageConnector("https://sas.example/test.pdf");

        var extractor = new FunctionHttpDocumentExtractor(
            new DocumentExtractionOptions { FunctionBaseUrl = "https://example.test/" },
            http,
            blob
        );

        var result = await extractor.ExtractAsync(new DocumentInput
        {
            BlobPath = "container/path/to/file.pdf",
            ContentType = "application/pdf",
            FileName = "file.pdf"
        }, modelHint: "ACORD125");

        result.Status.Should().Be(ExtractionStatus.Succeeded);
        handler.LastRequestBody.Should().Contain("\"documentUri\":\"https://sas.example/test.pdf\"");
        handler.LastRequestBody.Should().NotContain("container/path/to/file.pdf");
    }

    [Fact]
    public async Task ExtractAsync_ShouldSetPendingReview_WhenAnyFieldBelowThreshold()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new
            {
                documentId = "doc-1",
                providerOperationId = "op-1",
                status = "Succeeded",
                fields = new[]
                {
                    new { name = "x", value = "y", confidence = 0.5f, boundingBox = (object?)null, pageNumber = 1 }
                },
                tables = Array.Empty<object>(),
                pages = Array.Empty<object>(),
                overallConfidence = 0.5f
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var blob = new FakeBlobStorageConnector("https://sas.example/test.pdf");

        var extractor = new FunctionHttpDocumentExtractor(
            new DocumentExtractionOptions
            {
                FunctionBaseUrl = "https://example.test/",
                LowConfidenceThreshold = 0.75f
            },
            http,
            blob
        );

        var result = await extractor.ExtractAsync(new DocumentInput
        {
            DocumentUri = new Uri("https://doc.example/input.pdf"),
            ContentType = "application/pdf"
        });

        result.Status.Should().Be(ExtractionStatus.PendingReview);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class FakeBlobStorageConnector(string sasUrl) : IBlobStorageConnector
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BlobDownloadResult> DownloadAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BlobSasResult> GenerateSasUrlAsync(BlobSasRequest request, CancellationToken ct = default) => Task.FromResult(new BlobSasResult(sasUrl, DateTimeOffset.UtcNow.AddHours(1)));
        public Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ArchiveAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<BlobListItem> ListAsync(string prefix, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
