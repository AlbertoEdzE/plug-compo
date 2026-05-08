using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.DocumentClassification.Configuration;
using KSquare.DocumentClassification.Models;
using KSquare.DocumentClassification.Providers;

namespace KSquare.DocumentClassification.Tests;

public sealed class FunctionHttpDocumentClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_WithBlobPath_ShouldSendSasUrl_ToFunction()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new
            {
                documentType = "ACORD125",
                confidence = 0.92,
                method = "AzureDocumentClassifier",
                alternativeCandidates = Array.Empty<object>(),
                correlationId = (string?)null
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var blob = new FakeBlobStorageConnector("https://sas.example/test.pdf");

        var classifier = new FunctionHttpDocumentClassifier(
            new DocumentClassificationOptions { FunctionBaseUrl = "https://example.test/" },
            http,
            blob
        );

        var result = await classifier.ClassifyAsync(new DocumentInput
        {
            BlobPath = "container/path/to/file.pdf",
            ContentType = "application/pdf",
            FileName = "file.pdf",
            FirstPageText = "acord 125"
        });

        result.DocumentType.Should().Be("ACORD125");
        handler.LastRequestBody.Should().Contain("\"documentUri\":\"https://sas.example/test.pdf\"");
        handler.LastRequestBody.Should().NotContain("container/path/to/file.pdf");
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
