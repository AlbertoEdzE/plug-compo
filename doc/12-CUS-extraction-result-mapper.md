# Component 12 — Extraction Result Mapper

**Library**: `KSquare.ExtractionMapper`  
**Layer**: Intelligence  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

Azure Document Intelligence (Component 10) returns raw key-value pairs with provider-specific
field names (e.g. `"NamedInsured"`, `"named_insured"`, `"Named Insured"`). The workbench
domain model uses canonical field names (`InsuredName`, `InsuredAddress`, etc.).

The mapping between raw extraction output and domain model is:
- Non-trivial: field names vary by document type, model version, and training data
- Type-safe: raw strings must be parsed to typed values (dates, decimals, booleans)
- Configurable: business users change mappings without code deployment (YAML rules)
- Validatable: mapped values must satisfy domain constraints (required fields, formats)
- Auditable: every mapping decision must be traceable (which rule mapped which field)

Without this library, each service implements its own ad hoc mapping logic that becomes
unmaintainable as document types and model versions evolve.

---

## Interface Contract

```csharp
namespace KSquare.ExtractionMapper.Contracts;

public interface IExtractionMapper
{
    // Map a raw ExtractionResult to a typed domain model.
    MappingResult<T> Map<T>(ExtractionResult extraction, string documentType) where T : class, new();

    // Map to a dictionary of canonical field names → typed values (for dynamic schemas).
    MappingResult<IDictionary<string, object?>> MapToDictionary(ExtractionResult extraction, string documentType);
}

public interface IMappingRuleProvider
{
    // Load the mapping rule set for a given document type.
    Task<MappingRuleSet> GetRulesAsync(string documentType, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.ExtractionMapper.Models;

public record MappingResult<T>
{
    public required T Value { get; init; }
    public required IReadOnlyList<MappedField> MappedFields { get; init; }
    public required IReadOnlyList<MappingWarning> Warnings { get; init; }
    public bool HasLowConfidenceFields => MappedFields.Any(f => f.SourceConfidence < 0.75f);
    public bool HasUnmappedRequiredFields => Warnings.Any(w => w.Severity == WarningSeverity.RequiredFieldMissing);
}

public record MappedField
{
    public required string CanonicalFieldName { get; init; }
    public required string? SourceFieldName { get; init; }     // raw extraction field name
    public required object? Value { get; init; }
    public required float SourceConfidence { get; init; }
    public required string RuleApplied { get; init; }          // rule ID for audit
}

public record MappingWarning(
    string FieldName,
    string Message,
    WarningSeverity Severity
);

public enum WarningSeverity { Info, RequiredFieldMissing, LowConfidence, ParseFailure }

// A rule that maps one or more raw field names → one canonical field
public record FieldMappingRule
{
    public required string RuleId { get; init; }
    public required string CanonicalField { get; init; }
    public required IReadOnlyList<string> SourceFieldNames { get; init; }  // try in order
    public required string TargetType { get; init; }    // "string", "decimal", "date", "bool"
    public string? DefaultValue { get; init; }
    public bool Required { get; init; } = false;
    public string? TransformExpression { get; init; }   // simple: "Trim", "ToUpper", "ParseDate:MM/dd/yyyy"
}

public record MappingRuleSet
{
    public required string DocumentType { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<FieldMappingRule> Rules { get; init; }
}
```

---

## Configuration

```csharp
public class ExtractionMapperOptions
{
    public MappingRuleSource RuleSource { get; set; } = MappingRuleSource.EmbeddedYaml;
    public string? RulesBlobContainerName { get; set; } = "mapping-rules";
    public bool StrictMode { get; set; } = false;   // if true, throw on unmapped required fields
    public TimeSpan RuleCacheTtl { get; set; } = TimeSpan.FromMinutes(10);
}

public enum MappingRuleSource { EmbeddedYaml, BlobStorage, FileSystem }
```

---

## DI Registration

```csharp
builder.Services.AddKsExtractionMapper(options =>
{
    options.RuleSource = MappingRuleSource.BlobStorage;
    options.RulesBlobContainerName = "mapping-rules";
});
// Requires KSquare.BlobStorage if using BlobStorage rule source.
```

---

## Mapping Rules YAML Format

Stored as `{documentType}.mapping.yml` — one file per document type:

```yaml
# acord125.mapping.yml
document_type: ACORD125
version: "1.2"
rules:
  - rule_id: R001
    canonical_field: InsuredName
    source_fields: ["NamedInsured", "named_insured", "Insured Name", "InsuredName"]
    target_type: string
    transform: Trim
    required: true

  - rule_id: R002
    canonical_field: InsuredAddress
    source_fields: ["MailingAddress", "mailing_address", "Address"]
    target_type: string
    required: false

  - rule_id: R003
    canonical_field: PolicyEffectiveDate
    source_fields: ["EffectiveDate", "effective_date", "Policy Effective Date"]
    target_type: date
    transform: "ParseDate:MM/dd/yyyy"
    required: true

  - rule_id: R004
    canonical_field: TotalInsuredValue
    source_fields: ["TIV", "Total Insured Value", "TotalInsuredValue"]
    target_type: decimal
    required: false

  - rule_id: R005
    canonical_field: NumberOfLocations
    source_fields: ["NumberOfLocations", "Locations", "Num Locations"]
    target_type: int
    default_value: "1"
    required: false
```

---

## Domain Model Mapping Targets

```csharp
// Target model for ACORD 125 extraction → submission fields
public class Acord125ExtractedData
{
    public string? InsuredName { get; set; }
    public string? InsuredAddress { get; set; }
    public string? InsuredCity { get; set; }
    public string? InsuredState { get; set; }
    public string? InsuredZip { get; set; }
    public DateOnly? PolicyEffectiveDate { get; set; }
    public DateOnly? PolicyExpirationDate { get; set; }
    public decimal? TotalInsuredValue { get; set; }
    public int? NumberOfLocations { get; set; }
    public string? PrimaryNaicsCode { get; set; }
    public string? BrokerName { get; set; }
    public string? BrokerLicenseNumber { get; set; }
}
```

---

## Usage Example

```csharp
// After document extraction:
var extractionResult = await extractor.ExtractAsync(docInput, modelHint: "ACORD125");

// Map to typed domain model
var mappingResult = mapper.Map<Acord125ExtractedData>(extractionResult, "ACORD125");

// Use mapped values
var submissionData = mappingResult.Value;
log.LogInformation("Mapped InsuredName: {Name} (confidence: {Conf})",
    submissionData.InsuredName,
    mappingResult.MappedFields.FirstOrDefault(f => f.CanonicalFieldName == "InsuredName")?.SourceConfidence);

// Flag for review if needed
if (mappingResult.HasLowConfidenceFields || mappingResult.HasUnmappedRequiredFields)
{
    foreach (var warning in mappingResult.Warnings)
        log.LogWarning("Mapping warning [{Severity}] {Field}: {Msg}", warning.Severity, warning.FieldName, warning.Message);
}

// Or map to dictionary for dynamic downstream processing
var dictResult = mapper.MapToDictionary(extractionResult, "ACORD125");
```

---

## Transform Expressions

| Expression | Input | Output |
|---|---|---|
| `Trim` | `"  John Smith  "` | `"John Smith"` |
| `ToUpper` | `"new york"` | `"NEW YORK"` |
| `ParseDate:MM/dd/yyyy` | `"01/15/2025"` | `DateOnly(2025, 1, 15)` |
| `ParseDate:yyyy-MM-dd` | `"2025-01-15"` | `DateOnly(2025, 1, 15)` |
| `ParseDecimal` | `"$1,250,000"` | `1250000.00m` |
| `ParseBool` | `"Yes"` / `"No"` | `true` / `false` |
| `StripCurrency` | `"$1,250,000.00"` | `"1250000.00"` |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.ExtractionMapper at path: shared/KSquare.ExtractionMapper/

This library maps raw ExtractionResult (from KSquare.DocumentExtraction) to typed domain models
using YAML-defined field mapping rules. It is configuration-driven and requires no code change
when field names or document model versions change.

Project structure:
  shared/KSquare.ExtractionMapper/
  ├── KSquare.ExtractionMapper.csproj
  ├── Contracts/
  │   ├── IExtractionMapper.cs
  │   └── IMappingRuleProvider.cs
  ├── Models/
  │   ├── MappingResult.cs (generic)
  │   ├── MappedField.cs
  │   ├── MappingWarning.cs
  │   ├── WarningSeverity.cs (enum)
  │   ├── FieldMappingRule.cs
  │   └── MappingRuleSet.cs
  ├── Configuration/
  │   └── ExtractionMapperOptions.cs
  ├── Rules/
  │   ├── EmbeddedYamlRuleProvider.cs     ← loads .mapping.yml from embedded resources
  │   ├── BlobRuleProvider.cs             ← loads from Blob Storage with caching
  │   └── Resources/
  │       ├── acord125.mapping.yml
  │       ├── lossrun.mapping.yml
  │       └── financial.mapping.yml
  ├── Internal/
  │   ├── FieldMapper.cs                  ← core mapping engine
  │   └── TransformEngine.cs              ← applies transform expressions
  └── Extensions/
      └── ServiceCollectionExtensions.cs

FieldMapper.Map<T>:
  1. Load MappingRuleSet for documentType via IMappingRuleProvider
  2. For each FieldMappingRule:
     a. Try each source_fields name in order (case-insensitive) against extraction.Fields
     b. Take first match; if no match and default_value set, use default; if required, add Warning(RequiredFieldMissing)
     c. Apply TransformEngine with rule.TransformExpression
     d. Use reflection to set T.{CanonicalField} = converted value
     e. Record MappedField with source name, confidence from ExtractedField, rule ID
  3. Build MappingResult<T> with Value, MappedFields, Warnings

TransformEngine:
  - Implements each named transform (Trim, ToUpper, ParseDate, ParseDecimal, ParseBool, StripCurrency)
  - ParseDate accepts format string after colon: "ParseDate:MM/dd/yyyy"
  - StripCurrency: strip "$", ",", then parse as decimal
  - On parse failure: add MappingWarning(ParseFailure), return null for the field

EmbeddedYamlRuleProvider:
  - Use YamlDotNet to deserialize .mapping.yml embedded resources
  - Cache parsed MappingRuleSet in ConcurrentDictionary<string, MappingRuleSet>
  - Load from Assembly.GetManifestResourceStream

BlobRuleProvider:
  - Fetch {documentType}.mapping.yml from blob container using IBlobStorageConnector
  - Cache in IMemoryCache with RuleCacheTtl expiry
  - Fall back to embedded resource if blob fetch fails

Include the acord125.mapping.yml, lossrun.mapping.yml, and financial.mapping.yml files
as embedded resources with at least 5 rules each covering common field names.

NuGet packages:
  - YamlDotNet 13.x
  - Microsoft.Extensions.Caching.Memory

Tests at shared/KSquare.ExtractionMapper.Tests/:
  - R001 maps "NamedInsured" → InsuredName correctly
  - R001 tries fallback source field names (second/third alias) when first not present
  - Required field missing → MappingWarning(RequiredFieldMissing) in result
  - ParseDate transform parses MM/dd/yyyy correctly
  - ParseDecimal strips "$" and "," then converts to decimal
  - Low-confidence field (< 0.75) → MappingWarning(LowConfidence) and HasLowConfidenceFields = true
  - Full ACORD125 mapping round-trip test with 5 fields
  Use xUnit + FluentAssertions.
```
