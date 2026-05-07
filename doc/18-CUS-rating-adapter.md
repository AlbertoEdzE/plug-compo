# Component 18 — Rating Adapter

**Library**: `KSquare.RatingAdapter`  
**Layer**: Integration / Quote  
**Default Provider**: Customer-Provided Rating Engine (UE Rating API)  
**Alternate Providers**: ISO RateBook, Applied Rater, Mock (for demo)  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Quote generation requires calling an external rating engine to compute premiums per coverage line.
The rating engine is customer-provided (blue box in architecture), but the adapter that drives it is
owned by the platform team and is non-trivial:

- Input: a `CoveragePricingRequest` built from submission data (institution profile, coverage selections, loss history)
- Output: a `PremiumResult` with per-coverage-line premiums normalized into the internal quote model
- The rating engine's input schema is specific to the customer's system — mapping internal fields to rating input fields is substantial work
- Retry + circuit breaker: rating engines are often slow and throttled
- Response normalization: rating engines return proprietary formats; the adapter produces a provider-neutral `PremiumResult`
- The adapter is swappable — the same quote-api works whether the rating engine is UE Rating, ISO, or a future replacement

Without this library, quote-api hard-codes rating engine call logic, making provider changes a full service rewrite.

---

## Interface Contract

```csharp
namespace KSquare.RatingAdapter.Contracts;

public interface IRatingAdapter
{
    // Submit a pricing request to the rating engine.
    // Returns normalized premium results per coverage line.
    Task<RatingResult> RequestPricingAsync(
        CoveragePricingRequest request,
        CancellationToken ct = default);
}

public interface ICoveragePricingMapper
{
    // Map internal submission + coverage selections to rating engine input payload.
    RatingEngineInput MapToRatingInput(CoveragePricingRequest request);

    // Map rating engine response back to normalized PremiumResult.
    RatingResult MapFromRatingOutput(RatingEngineOutput output, string correlationId);
}
```

---

## Models

```csharp
namespace KSquare.RatingAdapter.Models;

// Input to the adapter — built by quote-api from submission data
public record CoveragePricingRequest
{
    public required string SubmissionId { get; init; }
    public required string QuoteId { get; init; }
    public required string InstitutionType { get; init; }       // "K-12 Public District"
    public required string NaicsCode { get; init; }
    public required string State { get; init; }
    public required int NumberOfLocations { get; init; }
    public required int TotalEnrollment { get; init; }
    public required int FteEmployees { get; init; }
    public required decimal TotalInsuredValue { get; init; }
    public required decimal OperatingExpenses { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required IReadOnlyList<CoverageLineRequest> CoverageLines { get; init; }
    public required LossHistorySummary LossHistory { get; init; }
    public string? CorrelationId { get; init; }
}

public record CoverageLineRequest
{
    public required string ProductCode { get; init; }     // "GL", "PROP", "ELL", "SA"
    public required string ProductName { get; init; }
    public required decimal RequestedLimit { get; init; }
    public required decimal RequestedRetention { get; init; }
    public decimal? RequestedAggregateLimit { get; init; }
}

public record LossHistorySummary
{
    public required decimal FiveYearAverageLossRatio { get; init; }
    public required decimal LargestSingleLoss { get; init; }
    public required int TotalClaimsCount { get; init; }
    public required int DataYearsAvailable { get; init; }
}

// Normalized output — provider-neutral
public record RatingResult
{
    public required string SubmissionId { get; init; }
    public required string QuoteId { get; init; }
    public required RatingStatus Status { get; init; }
    public required IReadOnlyList<CoverageLinePremium> PremiumLines { get; init; }
    public required decimal TotalAnnualPremium { get; init; }
    public string? RatingEngineReferenceId { get; init; }    // provider's internal ID
    public string? RatingBasis { get; init; }                // "Manual", "Experience", "Schedule"
    public IReadOnlyList<RatingMessage> Messages { get; init; } = [];
    public DateTimeOffset RatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}

public record CoverageLinePremium
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required decimal AnnualPremium { get; init; }
    public decimal? MinimumPremium { get; init; }
    public decimal? SurchargeAmount { get; init; }
    public decimal? CreditAmount { get; init; }
    public IReadOnlyList<RatingFactor> Factors { get; init; } = [];
}

public record RatingFactor(string FactorName, decimal Value, string Description);
public record RatingMessage(RatingMessageLevel Level, string Code, string Text);

public enum RatingStatus { Rated, RatingFailed, RequiresManualRating, Referral }
public enum RatingMessageLevel { Info, Warning, Error }

// Raw provider payload shapes — internal to adapter
public record RatingEngineInput(IDictionary<string, object?> Payload);
public record RatingEngineOutput(IDictionary<string, object?> Response, int HttpStatusCode);
```

---

## Configuration

```csharp
public class RatingAdapterOptions
{
    public RatingProvider Provider { get; set; } = RatingProvider.UeRatingEngine;

    // UE Rating Engine
    public string? RatingEngineBaseUrl { get; set; }
    public string? RatingEngineApiKey { get; set; }      // from Key Vault
    public string? RatingEngineVersion { get; set; } = "v2";

    // Timeout + Retry
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public int CircuitBreakerFailureThreshold { get; set; } = 5;  // open after 5 consecutive failures
    public TimeSpan CircuitBreakerResetTimeout { get; set; } = TimeSpan.FromMinutes(1);
}

public enum RatingProvider { UeRatingEngine, Mock }
```

---

## DI Registration

```csharp
builder.Services.AddKsRatingAdapter(options =>
{
    builder.Configuration.GetSection("KSquare:RatingAdapter").Bind(options);
    options.RatingEngineApiKey = builder.Configuration["RatingEngine--ApiKey"];
});
```

---

## Coverage Pricing Mapper — UE Rating Engine

The UE rating engine accepts a specific JSON schema. The mapper translates internal fields:

```csharp
public class UeRatingEnginePricingMapper : ICoveragePricingMapper
{
    public RatingEngineInput MapToRatingInput(CoveragePricingRequest request)
    {
        // UE Rating Engine input schema
        var payload = new Dictionary<string, object?>
        {
            ["requestId"]         = request.CorrelationId ?? Guid.NewGuid().ToString(),
            ["institutionType"]   = MapInstitutionType(request.InstitutionType),
            ["state"]             = request.State,
            ["naicsCode"]         = request.NaicsCode,
            ["effectiveDate"]     = request.EffectiveDate.ToString("yyyy-MM-dd"),
            ["expirationDate"]    = request.ExpirationDate.ToString("yyyy-MM-dd"),
            ["riskCharacteristics"] = new
            {
                totalInsuredValue    = request.TotalInsuredValue,
                totalEnrollment      = request.TotalEnrollment,
                fteEmployees         = request.FteEmployees,
                operatingExpenses    = request.OperatingExpenses,
                locationCount        = request.NumberOfLocations,
                fiveYearLossRatio    = request.LossHistory.FiveYearAverageLossRatio,
                largestSingleLoss    = request.LossHistory.LargestSingleLoss,
                priorClaimsCount     = request.LossHistory.TotalClaimsCount
            },
            ["coverageLines"] = request.CoverageLines.Select(c => new
            {
                lineCode   = c.ProductCode,
                limit      = c.RequestedLimit,
                retention  = c.RequestedRetention,
                aggregate  = c.RequestedAggregateLimit
            }).ToList()
        };

        return new RatingEngineInput(payload);
    }

    public RatingResult MapFromRatingOutput(RatingEngineOutput output, string correlationId)
    {
        var data = output.Response;

        if (output.HttpStatusCode != 200 || data.ContainsKey("error"))
        {
            return new RatingResult
            {
                SubmissionId = "",
                QuoteId = "",
                Status = RatingStatus.RatingFailed,
                PremiumLines = [],
                TotalAnnualPremium = 0,
                Messages = [new RatingMessage(RatingMessageLevel.Error, "RATING_FAILED", data.GetValueOrDefault("error")?.ToString() ?? "Unknown")]
            };
        }

        var lines = ((IEnumerable<dynamic>)data["premiumLines"]).Select(l =>
            new CoverageLinePremium
            {
                ProductCode    = l.lineCode,
                ProductName    = l.lineName,
                AnnualPremium  = (decimal)l.annualPremium,
                MinimumPremium = l.minimumPremium,
                SurchargeAmount = l.surcharge,
                CreditAmount   = l.credit
            }).ToList();

        return new RatingResult
        {
            SubmissionId           = correlationId,
            QuoteId                = correlationId,
            Status                 = RatingStatus.Rated,
            PremiumLines           = lines,
            TotalAnnualPremium     = lines.Sum(l => l.AnnualPremium),
            RatingEngineReferenceId = data["ratingReferenceId"]?.ToString(),
            RatingBasis            = data["ratingBasis"]?.ToString(),
            CorrelationId          = correlationId
        };
    }

    private static string MapInstitutionType(string internalType) => internalType switch
    {
        "K-12 Public District"  => "K12_PUBLIC",
        "Higher Ed"             => "HIGHER_ED",
        "Private School"        => "PRIVATE_SCHOOL",
        _                       => "OTHER"
    };
}
```

---

## UE Rating Engine HTTP Adapter

```csharp
public class UeRatingEngineAdapter : IRatingAdapter
{
    // Uses Polly retry + circuit breaker pipeline
    public async Task<RatingResult> RequestPricingAsync(CoveragePricingRequest request, CancellationToken ct)
    {
        var input = _mapper.MapToRatingInput(request);
        var json = JsonSerializer.Serialize(input.Payload);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/{_options.RatingEngineVersion}/rate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("X-Api-Key", _options.RatingEngineApiKey);
        httpRequest.Headers.Add("X-Correlation-Id", request.CorrelationId ?? "");

        // Polly pipeline: Retry(3, exponential) + CircuitBreaker(5 failures → open 1 min)
        var response = await _resiliencePipeline.ExecuteAsync(
            async token => await _httpClient.SendAsync(httpRequest, token), ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var output = new RatingEngineOutput(
            JsonSerializer.Deserialize<Dictionary<string, object?>>(responseBody) ?? [],
            (int)response.StatusCode
        );

        return _mapper.MapFromRatingOutput(output, request.CorrelationId ?? request.QuoteId);
    }
}
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Rating engine returns 429 (throttled) | Polly exponential backoff; max 3 retries |
| Rating engine returns 5xx | Retry; after threshold open circuit breaker for 1 minute |
| Circuit breaker open | Return `RatingStatus.RatingFailed` with `CIRCUIT_OPEN` message immediately |
| Rating engine returns `referral_required` flag | Return `RatingStatus.Referral`; quote-api triggers referral flow |
| Mapping exception (unknown field from rating response) | Log warning; use zero for unmapped premium; flag in Messages |
| Timeout (> 30s) | Cancel, return `RatingStatus.RatingFailed`, quote-api falls back to manual rating |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.RatingAdapter at path: shared/KSquare.RatingAdapter/

This library transforms internal coverage pricing requests into the UE Rating Engine's input format,
calls the rating engine HTTP API, and normalizes the response into a provider-neutral PremiumResult.
Used by ue-uw-quote-api to get premiums for each coverage line.

Project structure:
  shared/KSquare.RatingAdapter/
  ├── KSquare.RatingAdapter.csproj
  ├── Contracts/
  │   ├── IRatingAdapter.cs
  │   └── ICoveragePricingMapper.cs
  ├── Models/
  │   ├── CoveragePricingRequest.cs
  │   ├── CoverageLineRequest.cs
  │   ├── LossHistorySummary.cs
  │   ├── RatingResult.cs
  │   ├── CoverageLinePremium.cs
  │   ├── RatingFactor.cs
  │   ├── RatingMessage.cs
  │   ├── RatingStatus.cs (enum)
  │   ├── RatingEngineInput.cs
  │   └── RatingEngineOutput.cs
  ├── Configuration/
  │   └── RatingAdapterOptions.cs
  ├── Providers/
  │   ├── UeRatingEngineAdapter.cs          ← HTTP call with Polly pipeline
  │   └── MockRatingAdapter.cs              ← returns deterministic mock premiums for demo
  ├── Mapping/
  │   └── UeRatingEnginePricingMapper.cs    ← MapToRatingInput + MapFromRatingOutput
  └── Extensions/
      └── ServiceCollectionExtensions.cs

UeRatingEngineAdapter:
  - Use IHttpClientFactory (named client "rating-engine")
  - POST to {RatingEngineBaseUrl}/api/{Version}/rate
  - Add X-Api-Key and X-Correlation-Id headers
  - Build Polly ResiliencePipeline:
    - RetryStrategy: max 3 attempts, exponential backoff (2s, 4s, 8s), retry on HttpRequestException + 429 + 5xx
    - CircuitBreakerStrategy: open after 5 consecutive failures, reset after 1 minute
  - Deserialize response with System.Text.Json
  - On timeout (TaskCanceledException): return RatingStatus.RatingFailed with TIMEOUT message

MockRatingAdapter:
  - Returns deterministic premiums based on TotalInsuredValue
  - GL: TotalInsuredValue * 0.00068
  - Property: TotalInsuredValue * 0.00122
  - ELL: TotalInsuredValue * 0.00047
  - Student Accident: TotalEnrollment * 12.80
  - Return RatingStatus.Rated with these calculated values

ServiceCollectionExtensions.AddKsRatingAdapter:
  - Register IRatingAdapter based on Provider option
  - Register ICoveragePricingMapper
  - Configure named HttpClient "rating-engine" with base URL and timeout

NuGet packages:
  - Microsoft.Extensions.Http
  - Polly 8.x
  - Microsoft.Extensions.Http.Resilience

Tests at shared/KSquare.RatingAdapter.Tests/:
  - MockAdapter returns correct GL premium formula result
  - MockAdapter total equals sum of all line premiums
  - UeRatingEnginePricingMapper maps InstitutionType K-12 to "K12_PUBLIC"
  - UeRatingEnginePricingMapper maps 3 coverage lines to rating engine input correctly
  - MapFromRatingOutput extracts TotalAnnualPremium as sum of line premiums
  - MapFromRatingOutput returns RatingFailed when error key present in response
  Use xUnit + FluentAssertions + WireMock.Net (to mock the rating engine HTTP endpoint).
```
