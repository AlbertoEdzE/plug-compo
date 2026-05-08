using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.Correlation.Contracts;
using KSquare.DocumentExtraction.Configuration;
using KSquare.DocumentExtraction.Contracts;
using KSquare.DocumentExtraction.Models;
using Polly;
using Polly.Retry;

namespace KSquare.DocumentExtraction.Providers;

public sealed class FunctionHttpDocumentExtractor : IDocumentExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static FunctionHttpDocumentExtractor()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
    }

    private readonly DocumentExtractionOptions _options;
    private readonly HttpClient _http;
    private readonly IBlobStorageConnector _blob;
    private readonly ICorrelationContextAccessor? _correlation;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public FunctionHttpDocumentExtractor(
        DocumentExtractionOptions options,
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
            throw new InvalidOperationException("FunctionBaseUrl must be set for FunctionHttpDocumentExtractor.");
        }

        _http.BaseAddress ??= new Uri(options.FunctionBaseUrl.TrimEnd('/') + "/");
        _pipeline = BuildRetryPipeline(options);
    }

    public async Task<ExtractionResult> ExtractAsync(DocumentInput input, string? modelHint = null, CancellationToken ct = default)
    {
        input.Validate();

        var correlationId = _correlation?.Current?.CorrelationId;

        var request = new FunctionExtractRequest
        {
            BlobPath = input.BlobPath,
            DocumentUri = input.DocumentUri?.ToString(),
            ContentBase64 = input.Content is not null ? Convert.ToBase64String(input.Content) : null,
            ContentType = input.ContentType,
            FileName = input.FileName,
            ModelHint = modelHint,
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
                using var msg = new HttpRequestMessage(HttpMethod.Post, "api/extract")
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
                $"Document extraction function returned {(int)response.StatusCode}: {text}",
                null,
                response.StatusCode
            );
        }

        var result = await JsonSerializer.DeserializeAsync<ExtractionResult>(stream, JsonOptions, ct);
        if (result is null)
        {
            throw new InvalidOperationException("Failed to deserialize ExtractionResult.");
        }

        return ApplyConfidenceRouting(result);
    }

    private ExtractionResult ApplyConfidenceRouting(ExtractionResult result)
    {
        if (result.Fields.Count == 0 && result.Tables.Count == 0)
        {
            return result with { Status = ExtractionStatus.Failed };
        }

        if (result.Fields.Any(f => f.Confidence < _options.LowConfidenceThreshold))
        {
            return result with { Status = ExtractionStatus.PendingReview };
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

    private static ResiliencePipeline<HttpResponseMessage> BuildRetryPipeline(DocumentExtractionOptions options)
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

    private sealed class FunctionExtractRequest
    {
        public string? BlobPath { get; set; }
        public string? DocumentUri { get; set; }
        public string? ContentBase64 { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? ModelHint { get; set; }
        public string? CorrelationId { get; set; }
    }
}
