# Component 16 — Risk Analysis Engine

**Library**: `KSquare.RiskAnalysis`  
**Layer**: Intelligence / Domain Computation  
**Language**: C# / .NET 8  
**Depends On**: Component 10 (DocumentExtraction), Component 12 (ExtractionMapper), Component 14 (RulesEngine)

---

## Why This Is a Pluggable Component

The Submission Details screen displays computed intelligence that drives underwriting decisions:

- **Loss Experience table**: Year, Claims Count, Incurred ($), Loss Ratio (%) — populated from extracted loss run documents
- **5-Year Average Loss Ratio**: 21.0% — aggregated from the loss table
- **Risk Indicators**: Campus Safety Rating (88/100), Claims Severity (34/100), Policy Complexity (61/100), Litigation Exposure (22/100)
- **Appetite Fit Score**: 92% — shown in the submission header

None of these values are typed in by the user. They are computed from:
1. Extracted tables from loss run PDFs (Component 10/12)
2. Application form responses (field values entered during intake)
3. Submission coverage data (limits, locations, line of business)
4. Configurable scoring rules (business guidelines that change frequently)

Without a shared library:
- Each service that needs a risk score re-implements the computation
- Scoring rules are inconsistent across submission review and quote generation
- Rule changes require code deployments across multiple services
- The AG UI context builder (Component 13) has no clean way to retrieve structured risk data

Complexity justifying a library:
- Loss run table parsing: multi-year, multi-column, currency/percentage normalization
- Risk scoring is multi-factor with configurable weights per factor per institution type
- Appetite fit is a composite of multiple independent scores through a rules engine
- All scores must be traceable (which rules fired, which inputs drove which score)

---

## Interface Contract

```csharp
namespace KSquare.RiskAnalysis.Contracts;

public interface IRiskAnalysisEngine
{
    // Compute full risk analysis from submission data.
    // Reads extracted documents and submission fields from blob/API.
    Task<RiskAnalysisResult> AnalyzeAsync(
        RiskAnalysisRequest request,
        CancellationToken ct = default);
}

public interface ILossRunAnalyzer
{
    // Parse extracted loss run table rows into structured annual loss data.
    LossRunSummary Analyze(IReadOnlyList<ExtractedTable> lossTables, string submissionId);
}

public interface IRiskScorer
{
    // Compute all risk indicator scores from submission context.
    RiskIndicatorScores Score(RiskScoringContext context);
}

public interface IAppetiteCalculator
{
    // Compute appetite fit % using rules engine + weighted scores.
    Task<AppetiteFitResult> CalculateAsync(
        RiskIndicatorScores scores,
        RiskScoringContext context,
        CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.RiskAnalysis.Models;

public record RiskAnalysisRequest
{
    public required string SubmissionId { get; init; }
    public required string InstitutionType { get; init; }       // "K-12 Public District", "Higher Ed"
    public required string NaicsCode { get; init; }
    public required int NumberOfLocations { get; init; }
    public required decimal TotalInsuredValue { get; init; }
    public required int NumberOfCoverageLines { get; init; }
    public required IReadOnlyList<string> CoverageLineNames { get; init; }
    // Form responses as key-value (from ApplicationForm + FormResponse entities)
    public required IDictionary<string, string?> FormResponses { get; init; }
    // Extracted tables from loss run documents
    public IReadOnlyList<ExtractedTable> LossRunTables { get; init; } = [];
    public string? CorrelationId { get; init; }
}

public record RiskAnalysisResult
{
    public required string SubmissionId { get; init; }
    public required LossRunSummary LossRunSummary { get; init; }
    public required RiskIndicatorScores RiskIndicators { get; init; }
    public required AppetiteFitResult AppetiteFit { get; init; }
    public DateTimeOffset ComputedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}

// --- Loss Run ---

public record LossRunSummary
{
    public required IReadOnlyList<AnnualLossRecord> AnnualRecords { get; init; }
    public required decimal FiveYearAverageLossRatio { get; init; }
    public required int TotalClaimsCount { get; init; }
    public required decimal TotalIncurred { get; init; }
    public required LossTrend Trend { get; init; }
    public bool HasLitigatedClaims { get; init; }
    public decimal? LargestSingleLoss { get; init; }
    public int DataYearsAvailable { get; init; }
}

public record AnnualLossRecord
{
    public required int Year { get; init; }
    public required int ClaimsCount { get; init; }
    public required decimal TotalIncurred { get; init; }
    public required decimal LossRatio { get; init; }        // incurred / earned premium * 100
    public bool HasLitigatedClaims { get; init; }
}

public enum LossTrend { Improving, Stable, Worsening, Insufficient }

// --- Risk Indicators ---

public record RiskIndicatorScores
{
    public required ScoreWithFactors CampusSafetyRating { get; init; }     // 0–100
    public required ScoreWithFactors ClaimsSeverity { get; init; }         // 0–100
    public required ScoreWithFactors PolicyComplexity { get; init; }       // 0–100
    public required ScoreWithFactors LitigationExposure { get; init; }     // 0–100
    // Derived composite (not shown separately in UI but used for appetite calc)
    public decimal CompositeRiskScore => (
        CampusSafetyRating.Score * 0.30m +
        (100 - ClaimsSeverity.Score) * 0.30m +    // lower severity = better
        (100 - PolicyComplexity.Score) * 0.20m +   // lower complexity = better
        (100 - LitigationExposure.Score) * 0.20m   // lower litigation = better
    );
}

public record ScoreWithFactors
{
    public required int Score { get; init; }                               // 0–100
    public required string Label { get; init; }                            // "Low", "Medium", "High"
    public required IReadOnlyList<ScoringFactor> Factors { get; init; }
}

public record ScoringFactor(string Name, string Value, decimal WeightedContribution);

// --- Appetite Fit ---

public record AppetiteFitResult
{
    public required float Score { get; init; }              // 0.0–1.0 (shown as %)
    public required string Classification { get; init; }    // "In Appetite", "Borderline", "Out of Appetite"
    public required IReadOnlyList<string> FiredRules { get; init; }
    public required IReadOnlyList<string> RisksIdentified { get; init; }
    public bool RequiresReferral { get; init; }
    public string? ReferralReason { get; init; }
}
```

---

## Loss Run Table Parsing

```csharp
// Internal/LossRunTableParser.cs
// Converts raw ExtractedTable objects from Component 10/12 into AnnualLossRecord list

public class LossRunTableParser
{
    private static readonly string[] YEAR_COLUMNS   = ["year", "policy year", "year of loss"];
    private static readonly string[] CLAIMS_COLUMNS = ["claims", "claim count", "number of claims", "occurrences"];
    private static readonly string[] INCURRED_COLUMNS = ["incurred", "total incurred", "losses", "paid + reserved"];
    private static readonly string[] RATIO_COLUMNS  = ["ratio", "loss ratio", "l/r"];

    public IReadOnlyList<AnnualLossRecord> Parse(ExtractedTable table)
    {
        // Find column indices by matching header names (case-insensitive, fuzzy)
        var yearIdx    = FindColumn(table.Headers, YEAR_COLUMNS);
        var claimsIdx  = FindColumn(table.Headers, CLAIMS_COLUMNS);
        var incurredIdx = FindColumn(table.Headers, INCURRED_COLUMNS);
        var ratioIdx   = FindColumn(table.Headers, RATIO_COLUMNS);

        var records = new List<AnnualLossRecord>();
        foreach (var row in table.Rows)
        {
            // Skip totals/averages rows
            var yearCell = yearIdx >= 0 ? row[yearIdx] : null;
            if (yearCell == null || IsTotalsRow(yearCell)) continue;

            if (!TryParseYear(yearCell, out int year)) continue;

            records.Add(new AnnualLossRecord
            {
                Year = year,
                ClaimsCount    = ParseInt(SafeGet(row, claimsIdx)),
                TotalIncurred  = ParseCurrency(SafeGet(row, incurredIdx)),
                LossRatio      = ParsePercent(SafeGet(row, ratioIdx))
            });
        }

        return records.OrderBy(r => r.Year).ToList();
    }

    private static decimal ParseCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        // Strip $, commas, parentheses (loss runs often use (x) for negatives)
        var cleaned = Regex.Replace(value, @"[$,\s()]", "");
        return decimal.TryParse(cleaned, out var result) ? result : 0m;
    }

    private static decimal ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var cleaned = value.Replace("%", "").Trim();
        return decimal.TryParse(cleaned, out var result) ? result / 100m : 0m;
    }
}
```

---

## Risk Indicator Scoring

Each indicator is scored 0–100 using configurable weighted factors:

### Campus Safety Rating (higher = safer campus = better)

```csharp
// Scoring factors derived from application form responses:

CampusSafetyRating:
  + SecurityPersonnelOnSite (yes → +20)
  + SurveillanceCamerasInstalled (yes → +15)
  + EmergencyResponsePlanInPlace (yes → +20)
  + SafetyTrainingConductedAnnually (yes → +15)
  + IncidentReportingSystem (yes → +10)
  + CrisisManagementTeam (yes → +10)
  + SchoolResourceOfficer (yes → +10)
  - RecentSecurityIncidents (count > 3 → -30, 1-3 → -15, 0 → 0)
  - TrespassingIncidents (any → -10)
```

### Claims Severity (lower = better)

```csharp
ClaimsSeverity:
  Base score = 100 - NormalizedSeverityScore
  Where:
  NormalizedSeverityScore = f(LargestSingleLoss, TotalInsuredValue)
                          + f(FiveYearAvgIncurred, EarnedPremium)
                          + f(ClaimsCountPerLocation, IndustryAvg)

  // Higher claim sizes relative to TIV = higher severity score = worse
```

### Policy Complexity (lower = better)

```csharp
PolicyComplexity:
  + NumberOfLocations > 10 → +20
  + NumberOfLocations 5-10 → +10
  + NumberOfCoverageLines > 4 → +15
  + HasHighRiskCoverage (Athletic, SexualMisconduct, Cyber) → +20 per
  + MultiStateOperations → +15
  + InternationalExposure → +20
  Base = 0, cap at 100
```

### Litigation Exposure (lower = better)

```csharp
LitigationExposure:
  + HasLitigatedClaimsInHistory → +30
  + OperatesInHighLitigationState (CA, FL, NY, IL) → +20
  + HasSexualMisconductCoverage → +15
  + HasELL (Educators Legal Liability) → reference to past ELL claims
  + HasStudentBodyOver5000 → +10
  Base = 0, cap at 100
```

---

## Appetite Fit Calculation

```csharp
// Uses Component 14 (RulesEngine) with "appetite-scoring" rule set
// Combines RiskIndicatorScores into a 0–100% fit score

AppetiteFitCalculator:
  1. Compute CompositeRiskScore from RiskIndicatorScores
  2. Run "appetite-scoring" rules via IRulesEngine
     - Out-of-appetite NAICS → return 0% immediately
     - Known exclusions (e.g., > 25% of revenue from intercollegiate football) → 0%
  3. Apply modifiers from loss run:
     - FiveYearAvgLossRatio > 0.85 → multiply by 0.70
     - FiveYearAvgLossRatio > 0.65 → multiply by 0.85
     - FiveYearAvgLossRatio < 0.30 → multiply by 1.05 (cap at 1.0)
  4. Final score = CompositeRiskScore * RulesModifier
  5. Classification:
     - ≥ 80% → "In Appetite"
     - 60–79% → "Borderline — Referral Recommended"
     - < 60% → "Out of Appetite"
```

---

## Configuration

```csharp
public class RiskAnalysisOptions
{
    // Scoring weights per institution type
    public IDictionary<string, ScoringWeights> InstitutionTypeWeights { get; set; } = new Dictionary<string, ScoringWeights>
    {
        ["K-12 Public District"] = new ScoringWeights { CampusSafety = 0.30f, ClaimsSeverity = 0.30f, PolicyComplexity = 0.20f, LitigationExposure = 0.20f },
        ["Higher Ed"]            = new ScoringWeights { CampusSafety = 0.20f, ClaimsSeverity = 0.35f, PolicyComplexity = 0.25f, LitigationExposure = 0.20f },
    };

    // Loss ratio thresholds
    public decimal HighLossRatioThreshold { get; set; } = 0.65m;
    public decimal ExcessiveLossRatioThreshold { get; set; } = 0.85m;

    // Minimum years of loss history required before computing trend
    public int MinimumLossHistoryYears { get; set; } = 3;

    // Out-of-appetite NAICS codes
    public IList<string> OutOfAppetiteNaicsCodes { get; set; } = [];
}

public record ScoringWeights(float CampusSafety, float ClaimsSeverity, float PolicyComplexity, float LitigationExposure);
```

---

## DI Registration

```csharp
builder.Services.AddKsRiskAnalysis(options =>
{
    builder.Configuration.GetSection("KSquare:RiskAnalysis").Bind(options);
    options.OutOfAppetiteNaicsCodes = ["713210", "722511"];
})
// Requires KSquare.RulesEngine for appetite calculation.
;
```

---

## Usage Example

```csharp
// In ue-uw-underwriting-api after submission is received:
var analysisResult = await riskEngine.AnalyzeAsync(new RiskAnalysisRequest
{
    SubmissionId = submission.SubmissionId.ToString(),
    InstitutionType = institution.InstitutionType,
    NaicsCode = institution.NaicsCode,
    NumberOfLocations = institution.NumberOfLocations ?? 1,
    TotalInsuredValue = submission.TotalInsuredValue,
    NumberOfCoverageLines = submission.CoverageRequests.Count,
    CoverageLineNames = submission.CoverageRequests.Select(c => c.ProductName).ToList(),
    FormResponses = submission.FormResponses.ToDictionary(r => r.QuestionKey, r => r.Answer),
    LossRunTables = extractionResult?.Tables ?? [],
    CorrelationId = correlationId
});

// Store result and surface in Submission Details View
await submissionRepository.SaveRiskAnalysisAsync(submission.SubmissionId, analysisResult);

// The AG UI context builder (Component 13) calls:
// GET /api/submissions/{id}/risk-analysis → returns RiskAnalysisResult
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| No loss run documents attached | Compute indicators from form responses only; return `DataYearsAvailable = 0`; loss table empty |
| Loss run table extraction has low confidence | Use extracted data with warning flag; return `LossRunSummary.LowConfidenceData = true` |
| Out-of-appetite NAICS code | Return `AppetiteFit.Score = 0`, `Classification = "Out of Appetite"` immediately |
| Insufficient form responses to score safety | Return `CampusSafetyRating.Score = null` with `InsufficientData = true` |
| Rules engine unavailable | Return analysis without AppetiteFit; log and continue |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.RiskAnalysis at path: shared/KSquare.RiskAnalysis/

This library computes loss run summaries, risk indicator scores, and appetite fit percentage
from submission data and extracted document tables. It powers the Loss Experience table,
Risk Indicators card, and Appetite Fit score on the Submission Details screen.

Project structure:
  shared/KSquare.RiskAnalysis/
  ├── KSquare.RiskAnalysis.csproj
  ├── Contracts/
  │   ├── IRiskAnalysisEngine.cs
  │   ├── ILossRunAnalyzer.cs
  │   ├── IRiskScorer.cs
  │   └── IAppetiteCalculator.cs
  ├── Models/
  │   ├── RiskAnalysisRequest.cs
  │   ├── RiskAnalysisResult.cs
  │   ├── LossRunSummary.cs
  │   ├── AnnualLossRecord.cs
  │   ├── LossTrend.cs (enum)
  │   ├── RiskIndicatorScores.cs
  │   ├── ScoreWithFactors.cs
  │   ├── ScoringFactor.cs
  │   └── AppetiteFitResult.cs
  ├── Configuration/
  │   └── RiskAnalysisOptions.cs
  ├── Internal/
  │   ├── LossRunTableParser.cs       ← parse ExtractedTable rows into AnnualLossRecord list
  │   ├── LossRunAnalyzer.cs          ← aggregate into LossRunSummary with trend + averages
  │   ├── RiskScorerImpl.cs           ← compute 4 risk indicator scores from FormResponses + submission data
  │   ├── AppetiteCalculatorImpl.cs   ← combine scores via IRulesEngine "appetite-scoring" rule set
  │   └── ScoringHelpers.cs           ← ParseCurrency, ParsePercent, FindColumn helpers
  └── Extensions/
      └── ServiceCollectionExtensions.cs

LossRunTableParser:
  - Accept ExtractedTable (from KSquare.DocumentExtraction)
  - Detect year/claims/incurred/ratio column indices by fuzzy-matching headers
    (case-insensitive, check against known aliases: YEAR_COLUMNS, CLAIMS_COLUMNS, etc.)
  - Parse each data row: skip totals rows (detect "total", "average", "5-yr avg" in year cell)
  - ParseCurrency: strip $, commas, parentheses → decimal
  - ParsePercent: strip %, divide by 100 → decimal
  - TryParseYear: match 4-digit year (2019–2030) or policy year ranges ("2022-23")
  - Return List<AnnualLossRecord> ordered by year

LossRunAnalyzer:
  - Take List<AnnualLossRecord>
  - Compute FiveYearAverageLossRatio: weighted avg over last 5 years (or fewer if < 5 available)
  - Compute TotalClaimsCount, TotalIncurred
  - Determine LossTrend: compare last 2 years vs prior 3 years; if last2 avg < prior3 avg * 0.85 → Improving; > 1.15 → Worsening; else Stable; < 3 years data → Insufficient
  - Find LargestSingleLoss from incurred column
  - Set DataYearsAvailable = count of years

RiskScorerImpl:
  - Implement scoring for all 4 indicators using FormResponses dictionary
  - CampusSafetyRating: check known form response keys (see spec for factor list)
    Keys like "SecurityPersonnelOnSite", "SurveillanceCamerasInstalled", etc.
    Each is a yes/no response; apply positive/negative point adjustments; cap at 0–100
  - ClaimsSeverity: compute NormalizedSeverityScore from LossRunSummary.LargestSingleLoss / TotalInsuredValue
    Scale: LargestSingleLoss / TIV > 0.01 → high severity; < 0.001 → low severity
  - PolicyComplexity: score from NumberOfLocations, NumberOfCoverageLines, high-risk coverage flags
  - LitigationExposure: score from HasLitigatedClaims, HighLitigationState check, coverage type flags
  - Each indicator returns ScoreWithFactors including the list of factors that contributed

AppetiteCalculatorImpl:
  - Inject IRulesEngine
  - Build ReferralContext and run "appetite-scoring" rule set
  - If DeclineRule fires: return Score = 0.0, Classification = "Out of Appetite"
  - Else: compute composite score, apply loss ratio modifier, return AppetiteFitResult
  - Classification: >= 0.80 → "In Appetite"; 0.60–0.80 → "Borderline"; < 0.60 → "Out of Appetite"

Include the YAML rule set file for appetite-scoring at:
  shared/KSquare.RulesEngine/Resources/rules/appetite-scoring.yml
  with at least these rules:
  - OutOfAppetiteNaics → Decline if naics in out-of-appetite list
  - IntercollFBOver25Pct → Decline if intercollegiate football revenue > 25%
  - HighLossRatioModifier → multiply score by 0.70 if 5yr avg > 0.85
  - LowLossRatioBonus → multiply score by 1.05 if 5yr avg < 0.30

NuGet packages:
  - System.Text.RegularExpressions (built-in)
  - Reference: KSquare.RulesEngine, KSquare.DocumentExtraction

Tests at shared/KSquare.RiskAnalysis.Tests/:
  - LossRunTableParser parses year, claims, incurred, ratio from well-formed table
  - LossRunTableParser skips "Total" and "5-Year Average" rows
  - LossRunTableParser handles currency with $ and commas correctly
  - LossRunAnalyzer computes 5-year avg loss ratio correctly
  - LossRunAnalyzer returns Worsening trend when last 2 years > prior 3 years
  - RiskScorerImpl CampusSafety = 100 when all safety yes-responses present
  - RiskScorerImpl CampusSafety reduced by security incidents
  - PolicyComplexity increases with more coverage lines
  - AppetiteCalculator returns 0 for out-of-appetite NAICS code
  - AppetiteCalculator returns "In Appetite" for low-risk submission
  Use xUnit + FluentAssertions.
```
