using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.Correlation.Contracts;
using KSquare.DocumentClassification.Configuration;
using KSquare.DocumentClassification.Contracts;
using KSquare.DocumentClassification.Models;
using Polly;
using Polly.Retry;

namespace KSquare.DocumentClassification.Providers;

public sealed class FunctionHttpDocumentClassifier : IDocumentClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static FunctionHttpDocumentClassifier()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
    }

    private readonly DocumentClassificationOptions _options;
    private readonly HttpClient _http;
    private readonly IBlobStorageConnector _blob;
    private readonly ICorrelationContextAccessor? _correlation;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public FunctionHttpDocumentClassifier(
        DocumentClassificationOptions options,
        HttpClient http,
        IBlobStorageConnector blob,
        ICorrelationContextAccessor? correlation = null
    )
    {
        _options = options;
        _http = http;
        _blob = blob;
        _correlation = correlation;

        if (string.IsNullOrWhiteSpace(options.FunctionBaseUrl))
        {
            throw new InvalidOperationException("FunctionBaseUrl must be set for FunctionHttpDocumentClassifier.");
        }

        _http.BaseAddress ??= new Uri(options.FunctionBaseUrl.TrimEnd('/') + "/");
        _pipeline = BuildRetryPipeline(options);
    }

    public async Task<ClassificationResult> ClassifyAsync(DocumentInput input, CancellationToken ct = default)
    {
        input.Validate();
        var correlationId = _correlation?.Current?.CorrelationId;

        var request = new FunctionClassifyRequest
        {
            BlobPath = input.BlobPath,
            DocumentUri = input.DocumentUri?.ToString(),
            ContentBase64 = input.Content is not null ? Convert.ToBase64String(input.Content) : null,
            ContentType = input.ContentType,
            FileName = input.FileName,
            FirstPageText = input.FirstPageText,
            CorrelationId = correlationId
        };

        if (!string.IsNullOrWhiteSpace(input.BlobPath))
        {
            var sasUrl = await GetSasUrlAsync(input.BlobPath!, ct);
            request.BlobPath = null;
            request.DocumentUri = sasUrl;
        }

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        using var response = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, "api/classify")
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                };
                return await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var text = await new StreamReader(stream).ReadToEndAsync(ct);
            throw new HttpRequestException(
                $"Document classification function returned {(int)response.StatusCode}: {text}",
                null,
                response.StatusCode
            );
        }

        var result = await JsonSerializer.DeserializeAsync<ClassificationResult>(stream, JsonOptions, ct);
        if (result is null)
        {
            throw new InvalidOperationException("Failed to deserialize ClassificationResult.");
        }

        return result;
    }

    private async Task<string> GetSasUrlAsync(string canonicalBlobPath, CancellationToken ct)
    {
        var (container, blobPath) = ParseCanonicalBlobPath(canonicalBlobPath);
        var sas = await _blob.GenerateSasUrlAsync(new BlobSasRequest(
            container,
            blobPath,
            BlobSasPermissions.Read,
            _options.SasExpiry
        ), ct);
        return sas.SasUrl;
    }

    private static (string Container, string RelativePath) ParseCanonicalBlobPath(string blobPath)
    {
        var firstSlash = blobPath.IndexOf('/');
        if (firstSlash <= 0 || firstSlash >= blobPath.Length - 1)
        {
            throw new ArgumentException("BlobPath must be in the form 'containerName/relativePath'.", nameof(blobPath));
        }

        return (blobPath[..firstSlash], blobPath[(firstSlash + 1)..]);
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildRetryPipeline(DocumentClassificationOptions options)
    {
        var retry = new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = options.RetryBaseDelay,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500),
        };

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(retry)
            .Build();
    }

    private sealed class FunctionClassifyRequest
    {
        public string? BlobPath { get; set; }
        public string? DocumentUri { get; set; }
        public string? ContentBase64 { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? FirstPageText { get; set; }
        public string? CorrelationId { get; set; }
    }
}
