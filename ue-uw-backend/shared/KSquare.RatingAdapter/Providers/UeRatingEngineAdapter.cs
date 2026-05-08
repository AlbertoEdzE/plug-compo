using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KSquare.Correlation.Contracts;
using KSquare.RatingAdapter.Configuration;
using KSquare.RatingAdapter.Contracts;
using KSquare.RatingAdapter.Models;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace KSquare.RatingAdapter.Providers;

public sealed class UeRatingEngineAdapter : IRatingAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static UeRatingEngineAdapter()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
    }

    private readonly RatingAdapterOptions _options;
    private readonly ICoveragePricingMapper _mapper;
    private readonly ICorrelationContextAccessor? _correlation;
    private readonly HttpClient _http;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public UeRatingEngineAdapter(
        RatingAdapterOptions options,
        IHttpClientFactory httpClientFactory,
        ICoveragePricingMapper mapper,
        ICorrelationContextAccessor? correlation = null)
    {
        _options = options;
        _mapper = mapper;
        _correlation = correlation;
        _http = httpClientFactory.CreateClient("rating-engine");
        _pipeline = BuildPipeline(options);

        if (string.IsNullOrWhiteSpace(_options.RatingEngineVersion))
        {
            _options.RatingEngineVersion = "v2";
        }
    }

    public async Task<RatingResult> RequestPricingAsync(CoveragePricingRequest request, CancellationToken ct = default)
    {
        var correlationId = request.CorrelationId ?? _correlation?.Current?.CorrelationId ?? request.QuoteId;
        var input = _mapper.MapToRatingInput(request);
        var json = JsonSerializer.Serialize(input.Payload, JsonOptions);

        try
        {
            using var response = await _pipeline.ExecuteAsync(
                async token =>
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"api/{_options.RatingEngineVersion}/rate")
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };

                    if (!string.IsNullOrWhiteSpace(_options.RatingEngineApiKey))
                    {
                        httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", _options.RatingEngineApiKey);
                    }

                    httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                    return await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
                },
                ct
            );

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            Dictionary<string, object?> payload;
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody, JsonOptions) ?? new Dictionary<string, JsonElement>();
                payload = parsed.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["error"] = $"Failed to deserialize rating response (HTTP {(int)response.StatusCode})."
                };
            }

            var output = new RatingEngineOutput(payload, (int)response.StatusCode);
            var mapped = _mapper.MapFromRatingOutput(output, correlationId);

            return mapped with
            {
                SubmissionId = request.SubmissionId,
                QuoteId = request.QuoteId,
                CorrelationId = correlationId
            };
        }
        catch (BrokenCircuitException)
        {
            return new RatingResult
            {
                SubmissionId = request.SubmissionId,
                QuoteId = request.QuoteId,
                Status = RatingStatus.RatingFailed,
                PremiumLines = Array.Empty<CoverageLinePremium>(),
                TotalAnnualPremium = 0m,
                Messages = new[]
                {
                    new RatingMessage(RatingMessageLevel.Error, "CIRCUIT_OPEN", "Rating engine circuit breaker is open.")
                },
                CorrelationId = correlationId
            };
        }
        catch (TaskCanceledException)
        {
            return new RatingResult
            {
                SubmissionId = request.SubmissionId,
                QuoteId = request.QuoteId,
                Status = RatingStatus.RatingFailed,
                PremiumLines = Array.Empty<CoverageLinePremium>(),
                TotalAnnualPremium = 0m,
                Messages = new[]
                {
                    new RatingMessage(RatingMessageLevel.Error, "TIMEOUT", "Rating engine request timed out.")
                },
                CorrelationId = correlationId
            };
        }
        catch (HttpRequestException ex)
        {
            return new RatingResult
            {
                SubmissionId = request.SubmissionId,
                QuoteId = request.QuoteId,
                Status = RatingStatus.RatingFailed,
                PremiumLines = Array.Empty<CoverageLinePremium>(),
                TotalAnnualPremium = 0m,
                Messages = new[]
                {
                    new RatingMessage(RatingMessageLevel.Error, "HTTP_ERROR", ex.Message)
                },
                CorrelationId = correlationId
            };
        }
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(RatingAdapterOptions options)
    {
        var breaker = new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = 1.0,
            MinimumThroughput = options.CircuitBreakerFailureThreshold,
            SamplingDuration = options.CircuitBreakerResetTimeout,
            BreakDuration = options.CircuitBreakerResetTimeout,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
                .HandleResult(r => (int)r.StatusCode >= 500),
        };

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        if (options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
                    .HandleResult(r => (int)r.StatusCode >= 500),
            });
        }

        builder.AddCircuitBreaker(breaker);
        return builder.Build();
    }
}
