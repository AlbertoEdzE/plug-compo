using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Models;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Exceptions;
using KSquare.FormTemplates.FieldMaps;
using KSquare.FormTemplates.Models;
using Polly;
using Polly.Retry;

namespace KSquare.FormTemplates.Providers;

internal sealed class GhostDraftFormEngine(
    FormTemplateOptions options,
    FieldMapLoader maps,
    IBlobStorageConnector blobs,
    ICorrelationContextAccessor correlation,
    IHttpClientFactory httpClientFactory
) : FormTemplateEngineBase(options, maps, blobs, correlation)
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline = BuildPipeline();

    protected override async Task<byte[]> RenderCoreAsync(
        FieldMapDefinition map,
        FormRenderRequest request,
        string outputFormat,
        string? correlationId,
        CancellationToken ct
    )
    {
        _ = map;

        if (string.IsNullOrWhiteSpace(options.GhostDraftApiUrl))
        {
            throw new FormRenderException("GhostDraftApiUrl must be configured when using GhostDraft provider.");
        }

        var client = httpClientFactory.CreateClient("ghostdraft");
        if (client.BaseAddress is null)
        {
            client.BaseAddress = new Uri(options.GhostDraftApiUrl.TrimEnd('/') + "/");
        }

        var templateId = ResolveTemplateId(request.TemplateName);
        var body = new
        {
            templateId,
            fields = request.Fields,
            outputFormat = outputFormat
        };

        using var response = await _pipeline.ExecuteAsync(
            async token =>
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/render")
                {
                    Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
                };

                if (!string.IsNullOrWhiteSpace(options.GhostDraftApiKey))
                {
                    httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", options.GhostDraftApiKey);
                }

                if (!string.IsNullOrWhiteSpace(correlationId) && !httpRequest.Headers.Contains(CorrelationHeaders.CorrelationId))
                {
                    httpRequest.Headers.TryAddWithoutValidation(CorrelationHeaders.CorrelationId, correlationId);
                }

                return await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
            },
            ct
        );

        if (!response.IsSuccessStatusCode)
        {
            throw new FormRenderException($"GhostDraft render failed with status {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
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
}
