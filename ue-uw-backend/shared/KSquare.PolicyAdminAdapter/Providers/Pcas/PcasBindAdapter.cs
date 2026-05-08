using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Models;
using KSquare.EventBus.Contracts;
using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Database;
using KSquare.PolicyAdminAdapter.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace KSquare.PolicyAdminAdapter.Providers.Pcas;

public sealed class PcasBindAdapter(
    PolicyAdminAdapterOptions options,
    PolicyAdminDbContext db,
    IPolicyAdminPayloadBuilder payloadBuilder,
    IBindReadinessValidator readiness,
    IAuditTrailWriter audit,
    IEventPublisher publisher,
    IHttpClientFactory httpClientFactory,
    ILogger<PcasBindAdapter> logger
) : IPolicyAdminAdapter
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline = BuildPipeline();

    public Task<BindReadinessResult> ValidateBindReadinessAsync(BindRequest request, CancellationToken ct = default) =>
        readiness.ValidateAsync(request, ct);

    public async Task<BindJob> SubmitBindAsync(BindRequest request, CancellationToken ct = default)
    {
        var readinessResult = await readiness.ValidateAsync(request, ct);
        if (!readinessResult.IsReady)
        {
            var msg = string.Join("; ", readinessResult.Issues.Select(i => $"{i.Code}: {i.Message}"));
            throw new InvalidOperationException($"Bind readiness validation failed: {msg}");
        }

        var payload = payloadBuilder.Build(request);
        var payloadJson = JsonSerializer.Serialize(payload.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var jobId = "bind-" + Guid.NewGuid().ToString("N");
        var record = new BindJobRecord
        {
            BindJobId = jobId,
            QuoteId = request.QuoteId,
            SubmissionId = request.SubmissionId,
            Provider = PolicyAdminProvider.Pcas,
            Status = BindJobStatus.Pending,
            RetryCount = 0,
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.BindJobs.Add(record);
        await db.SaveChangesAsync(ct);

        var (transactionId, providerStatus) = await SubmitToProviderAsync(payload, request.CorrelationId, ct);

        record.ProviderTransactionId = transactionId;
        record.Status = providerStatus;
        await db.SaveChangesAsync(ct);

        await TryAuditAsync(
            resourceId: request.QuoteId,
            action: "BindSubmitted",
            correlationId: request.CorrelationId,
            actor: CreateActor(request.UnderwriterUserId),
            ct: ct
        );

        return ToModel(record);
    }

    public async Task<BindJob> GetBindStatusAsync(string bindJobId, CancellationToken ct = default)
    {
        var job = await db.BindJobs.AsNoTracking().FirstOrDefaultAsync(x => x.BindJobId == bindJobId, ct);
        if (job is null)
        {
            throw new KeyNotFoundException($"Bind job '{bindJobId}' was not found.");
        }

        return ToModel(job);
    }

    internal async Task<BindJobRecord?> GetJobForPollingAsync(string bindJobId, CancellationToken ct)
    {
        return await db.BindJobs.FirstOrDefaultAsync(x => x.BindJobId == bindJobId, ct);
    }

    internal async Task UpdateJobAsync(BindJobRecord record, CancellationToken ct)
    {
        db.BindJobs.Update(record);
        await db.SaveChangesAsync(ct);
    }

    internal async Task PublishBoundAsync(BindJobRecord record, BindRequest request, DateTimeOffset boundAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(record.PolicyNumber))
        {
            return;
        }

        var evt = new PolicyBoundEvent
        {
            QuoteId = record.QuoteId,
            SubmissionId = record.SubmissionId,
            BindJobId = record.BindJobId,
            PolicyNumber = record.PolicyNumber,
            EffectiveDate = request.EffectiveDate,
            ExpirationDate = request.ExpirationDate,
            TotalAnnualPremium = request.TotalAnnualPremium,
            BoundAt = boundAt,
            CorrelationId = request.CorrelationId
        };

        await publisher.PublishAsync(options.BoundEventTopic, nameof(PolicyBoundEvent), evt, ct: ct);
    }

    internal async Task PublishFailedAsync(BindJobRecord record, BindRequest request, DateTimeOffset failedAt, CancellationToken ct)
    {
        var evt = new BindFailedEvent
        {
            QuoteId = record.QuoteId,
            SubmissionId = record.SubmissionId,
            BindJobId = record.BindJobId,
            ErrorCode = record.ErrorCode ?? "PAM_ERROR",
            ErrorMessage = record.ErrorMessage ?? "Bind failed",
            FailedAt = failedAt,
            CorrelationId = request.CorrelationId
        };

        await publisher.PublishAsync(options.FailedEventTopic, nameof(BindFailedEvent), evt, ct: ct);
    }

    internal async Task<(string Status, string? PolicyNumber, string? ErrorCode, string? ErrorMessage)> GetProviderStatusAsync(
        string transactionId,
        string? correlationId,
        CancellationToken ct
    )
    {
        var client = CreateClient();
        var url = $"api/v2/policies/{transactionId}/status";

        using var response = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                AddHeaders(req, correlationId);
                return await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        if (!response.IsSuccessStatusCode)
        {
            return ("processing", null, "HTTP_" + (int)response.StatusCode, $"PCAS status returned {(int)response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<PcasStatusResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return (
            parsed?.Status ?? "processing",
            parsed?.PolicyNumber,
            parsed?.ErrorCode,
            parsed?.ErrorMessage
        );
    }

    internal async Task<(string TransactionId, BindJobStatus InitialStatus)> ResubmitAsync(string payloadJson, string? correlationId, CancellationToken ct)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? new Dictionary<string, object?>();
        var payload = new PolicyAdminPayload(dict);
        var (transactionId, status) = await SubmitToProviderAsync(payload, correlationId, ct);
        return (transactionId, status);
    }

    private async Task<(string TransactionId, BindJobStatus InitialStatus)> SubmitToProviderAsync(
        PolicyAdminPayload payload,
        string? correlationId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(options.PcasBaseUrl))
        {
            throw new InvalidOperationException("PcasBaseUrl must be configured.");
        }

        var client = CreateClient();

        using var response = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "api/v2/policies/bind")
                {
                    Content = JsonContent.Create(payload.Payload, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
                };
                AddHeaders(req, correlationId);
                return await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PCAS bind failed with {(int)response.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<PcasBindResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                     ?? throw new InvalidOperationException("PCAS bind response was empty.");

        var status = parsed.Status?.ToLowerInvariant() switch
        {
            "processing" => BindJobStatus.Processing,
            "submitted" => BindJobStatus.Submitted,
            _ => BindJobStatus.Submitted
        };

        return (parsed.TransactionId, status);
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("pcas");
        if (client.BaseAddress is null && !string.IsNullOrWhiteSpace(options.PcasBaseUrl))
        {
            client.BaseAddress = new Uri(options.PcasBaseUrl.TrimEnd('/') + "/");
        }

        return client;
    }

    private void AddHeaders(HttpRequestMessage request, string? correlationId)
    {
        if (!string.IsNullOrWhiteSpace(options.PcasApiKey) && !request.Headers.Contains("X-Api-Key"))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", options.PcasApiKey);
        }

        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains("X-Correlation-Id"))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        }
    }

    private static BindJob ToModel(BindJobRecord record)
    {
        return new BindJob
        {
            BindJobId = record.BindJobId,
            QuoteId = record.QuoteId,
            SubmissionId = record.SubmissionId,
            Status = record.Status,
            ProviderTransactionId = record.ProviderTransactionId,
            PolicyNumber = record.PolicyNumber,
            ErrorCode = record.ErrorCode,
            ErrorMessage = record.ErrorMessage,
            RetryCount = record.RetryCount,
            CreatedAt = record.CreatedAt,
            CompletedAt = record.CompletedAt
        };
    }

    private static AuditActor CreateActor(string? underwriterUserId)
    {
        if (!string.IsNullOrWhiteSpace(underwriterUserId))
        {
            return new AuditActor(underwriterUserId, underwriterUserId, ActorType: AuditActorType.User);
        }

        return new AuditActor("policy-admin-adapter", "Policy Admin Adapter", ActorType: AuditActorType.ServiceAccount);
    }

    private async Task TryAuditAsync(string resourceId, string action, string? correlationId, AuditActor actor, CancellationToken ct)
    {
        try
        {
            await audit.WriteAsync(new AuditEntry
            {
                ResourceType = "Quote",
                ResourceId = resourceId,
                Action = action,
                Actor = actor,
                CorrelationId = correlationId,
                ServiceName = "KSquare.PolicyAdminAdapter",
                OccurredAt = DateTimeOffset.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AuditTrail write failed for action {Action}", action);
        }
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline()
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

    private sealed record PcasBindResponse(string TransactionId, string? Status);
    private sealed record PcasStatusResponse(string? Status, string? PolicyNumber, string? ErrorCode, string? ErrorMessage);
}

