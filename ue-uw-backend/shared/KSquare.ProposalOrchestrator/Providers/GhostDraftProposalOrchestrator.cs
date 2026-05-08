using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.Correlation.Contracts;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Models;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Models;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Contracts;
using KSquare.ProposalOrchestrator.Database;
using KSquare.ProposalOrchestrator.Exceptions;
using KSquare.ProposalOrchestrator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace KSquare.ProposalOrchestrator.Providers;

public sealed class GhostDraftProposalOrchestrator : IProposalOrchestrator
{
    private static readonly ActivitySource ActivitySource = new("KSquare.ProposalOrchestrator");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static GhostDraftProposalOrchestrator()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
    }

    private readonly ProposalOrchestratorOptions _options;
    private readonly ProposalDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProposalPayloadBuilder _payloadBuilder;
    private readonly IBlobStorageConnector _blobs;
    private readonly IEventPublisher _events;
    private readonly IIdempotencyGuard _idempotency;
    private readonly ICorrelationContextAccessor? _correlation;
    private readonly ILogger<GhostDraftProposalOrchestrator> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public GhostDraftProposalOrchestrator(
        ProposalOrchestratorOptions options,
        ProposalDbContext db,
        IHttpClientFactory httpClientFactory,
        IProposalPayloadBuilder payloadBuilder,
        IBlobStorageConnector blobs,
        IEventPublisher events,
        IIdempotencyGuard idempotency,
        ILogger<GhostDraftProposalOrchestrator> logger,
        ICorrelationContextAccessor? correlation = null)
    {
        _options = options;
        _db = db;
        _httpClientFactory = httpClientFactory;
        _payloadBuilder = payloadBuilder;
        _blobs = blobs;
        _events = events;
        _idempotency = idempotency;
        _logger = logger;
        _correlation = correlation;
        _pipeline = BuildPipeline(options);
    }

    public async Task<ProposalGenerationJob> StartGenerationAsync(ProposalGenerationRequest request, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("proposal.start_generation", ActivityKind.Internal);

        var correlationId = request.CorrelationId ?? _correlation?.Current?.CorrelationId ?? Guid.NewGuid().ToString();
        var idempotencyKey = BuildIdempotencyKey(request.QuoteId, request.ProposalType);

        var existing = await _idempotency.GetAsync(idempotencyKey, ct);
        if (existing is not null && TryParseIdempotency(existing.ResponseBody, out var saved) && !string.IsNullOrWhiteSpace(saved.JobId))
        {
            var job = await _db.ProposalJobs.AsNoTracking().FirstOrDefaultAsync(x => x.JobId == saved.JobId, ct);
            if (job is not null)
            {
                return MapToModel(job);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ProposalType) &&
            (! _options.TemplateIdMap.TryGetValue(request.ProposalType, out var templateId) || string.IsNullOrWhiteSpace(templateId)))
        {
            throw new ProposalTemplateNotFoundException(request.ProposalType);
        }

        var payload = _payloadBuilder.Build(request);
        var json = JsonSerializer.Serialize(payload.Payload, JsonOptions);

        var http = CreateHttpClient();

        using var response = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v3/documents/generate")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_options.GhostDraftApiKey))
                {
                    httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", _options.GhostDraftApiKey);
                }

                httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                return await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        var generate = JsonSerializer.Deserialize<GhostDraftGenerateResponse>(body, JsonOptions);
        if (generate is null || string.IsNullOrWhiteSpace(generate.JobId))
        {
            throw new InvalidOperationException("GhostDraft response did not contain jobId.");
        }

        var jobId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;

        var record = new ProposalJobRecord
        {
            JobId = jobId,
            QuoteId = request.QuoteId,
            SubmissionId = request.SubmissionId,
            ProposalType = request.ProposalType,
            Provider = ProposalProvider.GhostDraft.ToString(),
            ProviderJobId = generate.JobId,
            Status = ProposalJobStatus.Pending,
            RetryCount = 0,
            CreatedAt = createdAt
        };

        _db.ProposalJobs.Add(record);
        await _db.SaveChangesAsync(ct);

        var idem = new ProposalIdempotencyRecord(jobId, correlationId, request);
        var idemJson = JsonSerializer.Serialize(idem, JsonOptions);
        await _idempotency.SetAsync(idempotencyKey, new IdempotencyResult(200, idemJson, "application/json", DateTimeOffset.UtcNow), null, ct);

        return MapToModel(record);
    }

    public async Task<ProposalGenerationJob> GetJobStatusAsync(string jobId, CancellationToken ct = default)
    {
        var job = await _db.ProposalJobs.AsNoTracking().FirstOrDefaultAsync(x => x.JobId == jobId, ct);
        if (job is null)
        {
            throw new InvalidOperationException($"Job '{jobId}' not found.");
        }

        return MapToModel(job);
    }

    public async Task<ProposalArtifact> CompleteJobAsync(string jobId, string providerDocumentUrl, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("proposal.complete_job", ActivityKind.Internal);

        var job = await _db.ProposalJobs.FirstOrDefaultAsync(x => x.JobId == jobId, ct);
        if (job is null)
        {
            throw new InvalidOperationException($"Job '{jobId}' not found.");
        }

        if (job.Status == ProposalJobStatus.Completed && !string.IsNullOrWhiteSpace(job.ArtifactBlobPath))
        {
            return await RefreshSasAsync(job, ct);
        }

        var correlationId = await TryGetCorrelationIdAsync(job, ct) ?? _correlation?.Current?.CorrelationId ?? Guid.NewGuid().ToString();
        var request = await TryGetOriginalRequestAsync(job, ct);

        var outputFormat = request?.OutputFormat ?? "pdf";
        var (contentType, extension) = GetContentTypeAndExtension(outputFormat);

        var fileName = $"{job.ProposalType}-{job.CreatedAt.UtcDateTime:yyyyMMddHHmmss}-{job.QuoteId}.{extension}";
        var blobPath = BuildOutputPath(_options.OutputPathTemplate, job, outputFormat);

        var http = CreateHttpClient();

        using var downloadResponse = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, providerDocumentUrl);
                if (!string.IsNullOrWhiteSpace(_options.GhostDraftApiKey))
                {
                    httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", _options.GhostDraftApiKey);
                }

                httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                return await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        downloadResponse.EnsureSuccessStatusCode();
        await using var downloadStream = await downloadResponse.Content.ReadAsStreamAsync(ct);
        await using var ms = new MemoryStream();
        await downloadStream.CopyToAsync(ms, ct);
        ms.Position = 0;

        var exists = await _blobs.ExistsAsync($"{_options.OutputBlobContainer}/{blobPath}", ct);
        if (!exists)
        {
            await _blobs.UploadAsync(
                new BlobUploadRequest(
                    _options.OutputBlobContainer,
                    blobPath,
                    ms,
                    contentType,
                    new Dictionary<string, string>
                    {
                        ["quoteId"] = job.QuoteId,
                        ["proposalType"] = job.ProposalType,
                        ["jobId"] = job.JobId
                    }
                ),
                ct
            );
        }

        var sasExpiry = DateTimeOffset.UtcNow.Add(_options.SasUrlTtl);
        string sasUrl = "";
        try
        {
            var sas = await _blobs.GenerateSasUrlAsync(
                new BlobSasRequest(
                    _options.OutputBlobContainer,
                    blobPath,
                    BlobSasPermissions.Read,
                    _options.SasUrlTtl,
                    $"attachment; filename=\"{fileName}\""
                ),
                ct
            );
            sasUrl = sas.SasUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate SAS URL for proposal job {JobId}", job.JobId);
        }

        job.Status = ProposalJobStatus.Completed;
        job.ArtifactBlobPath = $"{_options.OutputBlobContainer}/{blobPath}";
        job.ArtifactSasUrl = sasUrl;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);

        var evt = new ProposalGenerationCompletedEvent
        {
            QuoteId = job.QuoteId,
            SubmissionId = job.SubmissionId,
            JobId = job.JobId,
            BlobPath = job.ArtifactBlobPath,
            SasUrl = sasUrl,
            ProposalType = job.ProposalType,
            CompletedAt = job.CompletedAt.Value,
            CorrelationId = correlationId
        };

        await _events.PublishAsync(
            _options.CompletionEventTopic,
            "proposal.generation_completed",
            evt,
            new EventPublishOptions { CorrelationId = correlationId, MessageId = job.JobId },
            ct
        );

        return new ProposalArtifact
        {
            JobId = job.JobId,
            QuoteId = job.QuoteId,
            BlobPath = job.ArtifactBlobPath,
            SasUrl = sasUrl,
            SasExpiry = sasExpiry,
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = ms.Length
        };
    }

    internal async Task<GhostDraftStatusResponse> GetProviderStatusAsync(string providerJobId, string correlationId, CancellationToken ct)
    {
        var http = CreateHttpClient();

        using var response = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"api/v3/documents/{Uri.EscapeDataString(providerJobId)}/status");
                if (!string.IsNullOrWhiteSpace(_options.GhostDraftApiKey))
                {
                    httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", _options.GhostDraftApiKey);
                }

                httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                return await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<GhostDraftStatusResponse>(body, JsonOptions) ?? new GhostDraftStatusResponse();
    }

    internal async Task<(ProposalGenerationRequest? Request, string? CorrelationId)> GetIdempotencyDataAsync(ProposalJobRecord job, CancellationToken ct)
    {
        var key = BuildIdempotencyKey(job.QuoteId, job.ProposalType);
        var existing = await _idempotency.GetAsync(key, ct);
        if (existing is null)
        {
            return (null, null);
        }

        if (!TryParseIdempotency(existing.ResponseBody, out var parsed))
        {
            return (null, null);
        }

        return (parsed.Request, parsed.CorrelationId);
    }

    internal async Task<string?> ResubmitAsync(ProposalJobRecord job, CancellationToken ct)
    {
        var (request, correlationId) = await GetIdempotencyDataAsync(job, ct);
        if (request is null)
        {
            return null;
        }

        var corr = correlationId ?? request.CorrelationId ?? _correlation?.Current?.CorrelationId ?? Guid.NewGuid().ToString();
        var payload = _payloadBuilder.Build(request);
        var json = JsonSerializer.Serialize(payload.Payload, JsonOptions);

        var http = CreateHttpClient();

        using var response = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v3/documents/generate")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_options.GhostDraftApiKey))
                {
                    httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", _options.GhostDraftApiKey);
                }

                httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", corr);
                return await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        var generate = JsonSerializer.Deserialize<GhostDraftGenerateResponse>(body, JsonOptions);
        return generate?.JobId;
    }

    internal async Task PublishFailedAsync(ProposalJobRecord job, string errorMessage, string correlationId, CancellationToken ct)
    {
        var evt = new ProposalGenerationFailedEvent
        {
            QuoteId = job.QuoteId,
            SubmissionId = job.SubmissionId,
            JobId = job.JobId,
            ProposalType = job.ProposalType,
            ErrorMessage = errorMessage,
            FailedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId
        };

        await _events.PublishAsync(
            _options.CompletionEventTopic,
            "proposal.generation_failed",
            evt,
            new EventPublishOptions { CorrelationId = correlationId, MessageId = job.JobId },
            ct
        );
    }

    private HttpClient CreateHttpClient()
    {
        var http = _httpClientFactory.CreateClient("ghostdraft");
        if (http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.GhostDraftApiUrl))
        {
            http.BaseAddress = new Uri(_options.GhostDraftApiUrl.TrimEnd('/') + "/");
        }
        return http;
    }

    private async Task<string?> TryGetCorrelationIdAsync(ProposalJobRecord job, CancellationToken ct)
    {
        var (_, correlationId) = await GetIdempotencyDataAsync(job, ct);
        return correlationId;
    }

    private async Task<ProposalGenerationRequest?> TryGetOriginalRequestAsync(ProposalJobRecord job, CancellationToken ct)
    {
        var (request, _) = await GetIdempotencyDataAsync(job, ct);
        return request;
    }

    private async Task<ProposalArtifact> RefreshSasAsync(ProposalJobRecord job, CancellationToken ct)
    {
        var correlationId = await TryGetCorrelationIdAsync(job, ct) ?? _correlation?.Current?.CorrelationId ?? Guid.NewGuid().ToString();
        var request = await TryGetOriginalRequestAsync(job, ct);
        var outputFormat = request?.OutputFormat ?? "pdf";
        var (contentType, extension) = GetContentTypeAndExtension(outputFormat);

        var blobPath = job.ArtifactBlobPath!;
        var container = _options.OutputBlobContainer;
        var innerPath = blobPath.StartsWith(container + "/", StringComparison.OrdinalIgnoreCase)
            ? blobPath.Substring(container.Length + 1)
            : blobPath;

        var fileName = $"{job.ProposalType}-{job.CreatedAt.UtcDateTime:yyyyMMddHHmmss}-{job.QuoteId}.{extension}";

        var sasExpiry = DateTimeOffset.UtcNow.Add(_options.SasUrlTtl);
        var sas = await _blobs.GenerateSasUrlAsync(
            new BlobSasRequest(container, innerPath, BlobSasPermissions.Read, _options.SasUrlTtl, $"attachment; filename=\"{fileName}\""),
            ct
        );

        job.ArtifactSasUrl = sas.SasUrl;
        await _db.SaveChangesAsync(ct);

        return new ProposalArtifact
        {
            JobId = job.JobId,
            QuoteId = job.QuoteId,
            BlobPath = blobPath,
            SasUrl = sas.SasUrl,
            SasExpiry = sasExpiry,
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = 0
        };
    }

    private static ProposalGenerationJob MapToModel(ProposalJobRecord record)
    {
        return new ProposalGenerationJob
        {
            JobId = record.JobId,
            QuoteId = record.QuoteId,
            SubmissionId = record.SubmissionId,
            Status = record.Status,
            ProviderJobId = record.ProviderJobId,
            ArtifactBlobPath = record.ArtifactBlobPath,
            ArtifactSasUrl = record.ArtifactSasUrl,
            RetryCount = record.RetryCount,
            ErrorMessage = record.ErrorMessage,
            CreatedAt = record.CreatedAt,
            CompletedAt = record.CompletedAt
        };
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(ProposalOrchestratorOptions options)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .HandleResult(r => r.StatusCode == HttpStatusCode.RequestTimeout)
                .HandleResult(r => (int)r.StatusCode >= 500),
        });

        return builder.Build();
    }

    private static string BuildIdempotencyKey(string quoteId, string proposalType)
    {
        return $"proposal:{quoteId}:{proposalType}";
    }

    private static bool TryParseIdempotency(string responseBody, out ProposalIdempotencyRecord record)
    {
        try
        {
            record = JsonSerializer.Deserialize<ProposalIdempotencyRecord>(responseBody, JsonOptions) ?? new ProposalIdempotencyRecord("", "", null);
            return !string.IsNullOrWhiteSpace(record.JobId);
        }
        catch
        {
            record = new ProposalIdempotencyRecord("", "", null);
            return false;
        }
    }

    private static string BuildOutputPath(string template, ProposalJobRecord job, string outputFormat)
    {
        var timestamp = job.CreatedAt.UtcDateTime.ToString("yyyyMMddHHmmss");
        var year = job.CreatedAt.UtcDateTime.Year.ToString("D4");
        var month = job.CreatedAt.UtcDateTime.Month.ToString("D2");

        var path = template
            .Replace("{year}", year, StringComparison.OrdinalIgnoreCase)
            .Replace("{month}", month, StringComparison.OrdinalIgnoreCase)
            .Replace("{quoteId}", job.QuoteId, StringComparison.OrdinalIgnoreCase)
            .Replace("{proposalType}", job.ProposalType, StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", timestamp, StringComparison.OrdinalIgnoreCase);

        var desiredExt = "." + GetContentTypeAndExtension(outputFormat).Extension;
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !desiredExt.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - 4) + desiredExt;
        }

        return path.TrimStart('/');
    }

    private static (string ContentType, string Extension) GetContentTypeAndExtension(string outputFormat)
    {
        return outputFormat.Equals("docx", StringComparison.OrdinalIgnoreCase)
            ? ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx")
            : ("application/pdf", "pdf");
    }

    private sealed class GhostDraftGenerateResponse
    {
        public string? JobId { get; set; }
        public string? Status { get; set; }
    }

    internal sealed class GhostDraftStatusResponse
    {
        public string? Status { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed record ProposalIdempotencyRecord(string JobId, string CorrelationId, ProposalGenerationRequest? Request);
}

