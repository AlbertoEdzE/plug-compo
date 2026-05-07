# Component 06 — PII Redaction Filter

**Library**: `KSquare.PiiRedaction`  
**Layer**: Security and Cross-Cutting  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

The UW workbench handles sensitive data: broker emails, insured names, tax IDs, SSNs in
application forms, loss run financials, personal addresses. This data must not leak into:
- Application logs (Serilog / Application Insights)
- Service Bus event payloads (visible in Service Bus Explorer)
- Audit trail before/after snapshots

A shared PII filter library ensures every service uses the same redaction rules without
copy-pasting regex patterns.

Complexity justifying a library:
- JSON object deep scan with field name matching (case-insensitive, configurable list)
- Regex-based value detection (email pattern, phone, SSN format) regardless of field name
- Serilog sink / ILogger enricher integration
- Opt-in masking on specific serialization calls
- Configurable redaction token (`***REDACTED***` vs `[PII]`)

---

## Interface Contract

```csharp
namespace KSquare.PiiRedaction.Contracts;

public interface IPiiRedactor
{
    // Redact a JSON string by field name and value patterns
    string RedactJson(string json);

    // Redact a plain string value if it matches PII patterns
    string RedactValue(string value);

    // Check whether a field name is considered PII
    bool IsPiiField(string fieldName);
}
```

---

## Configuration

```csharp
public class PiiRedactionOptions
{
    // Field names (case-insensitive) that are always redacted
    public IList<string> PiiFieldNames { get; set; } = new List<string>
    {
        "email", "phone", "mobile", "taxId", "ssn", "ein", "driverLicense",
        "dateOfBirth", "dob", "password", "secret", "creditCard", "bankAccount",
        "routingNumber", "nationalId", "passportNumber", "address", "zipCode"
    };

    // Value-pattern detection (applied regardless of field name)
    public bool DetectEmailPatterns { get; set; } = true;
    public bool DetectPhonePatterns { get; set; } = true;
    public bool DetectSsnPatterns { get; set; } = true;

    public string RedactionToken { get; set; } = "***REDACTED***";
}
```

---

## DI Registration

```csharp
// Program.cs
builder.Services.AddKsPiiRedaction(options =>
{
    options.PiiFieldNames.Add("brokerEmail");
    options.PiiFieldNames.Add("contactPhone");
    options.RedactionToken = "[REDACTED]";
});

// Optional: integrate with Serilog (destructuring policy)
Log.Logger = new LoggerConfiguration()
    .Destructure.WithKsPiiRedaction(sp.GetRequiredService<IPiiRedactor>())
    .CreateLogger();
```

---

## Usage Examples

```csharp
// Redact before writing to audit trail
var maskedBefore = piiRedactor.RedactJson(JsonSerializer.Serialize(oldBroker));
var maskedAfter  = piiRedactor.RedactJson(JsonSerializer.Serialize(newBroker));

// Redact before publishing to Service Bus
var eventPayload = JsonSerializer.Serialize(submissionEvent);
var safePayload  = piiRedactor.RedactJson(eventPayload);

// Check individual field
if (!piiRedactor.IsPiiField(fieldName))
    log.LogDebug("Field {Name} = {Value}", fieldName, value);
```

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.PiiRedaction at path: shared/KSquare.PiiRedaction/

Project structure:
  shared/KSquare.PiiRedaction/
  ├── KSquare.PiiRedaction.csproj
  ├── Contracts/
  │   └── IPiiRedactor.cs
  ├── Configuration/
  │   └── PiiRedactionOptions.cs
  ├── Internal/
  │   ├── JsonPiiRedactor.cs          ← walks JSON tree, redacts by field name + value patterns
  │   └── PiiPatterns.cs              ← compiled Regex patterns for email, phone, SSN
  ├── Serilog/
  │   └── PiiRedactionDestructuringPolicy.cs  ← Serilog IDestructuringPolicy integration
  └── Extensions/
      └── ServiceCollectionExtensions.cs

JsonPiiRedactor implementation:
  - Use System.Text.Json.Nodes (JsonNode) to walk the JSON tree
  - For each JsonObject property:
    - If property name (case-insensitive) is in PiiFieldNames: replace value with redaction token
    - Else if DetectEmailPatterns and value matches email regex: replace value
    - Else if DetectPhonePatterns and value matches phone regex: replace value
    - Else if DetectSsnPatterns and value matches SSN regex (###-##-####): replace value
    - Recurse into nested objects and arrays
  - Return modified JSON string
  - Handle null/invalid JSON gracefully (return input unchanged, log warning)

PiiPatterns (compiled Regex):
  Email:  @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}"
  Phone:  @"\b(\+?1?\s?)?(\(?\d{3}\)?[\s.\-]?)?\d{3}[\s.\-]?\d{4}\b"
  SSN:    @"\b\d{3}-\d{2}-\d{4}\b"
  Use RegexOptions.Compiled for performance.

PiiRedactionDestructuringPolicy (Serilog):
  Implement Serilog.Core.IDestructuringPolicy
  In TryDestructure: if value is string, call redactor.RedactValue; if dict/object, serialize and call RedactJson

ServiceCollectionExtensions:
  AddKsPiiRedaction(Action<PiiRedactionOptions>) registers IPiiRedactor as singleton
  LoggerConfigurationExtensions: .WithKsPiiRedaction(IPiiRedactor) extension on Serilog LoggerConfiguration

NuGet packages:
  - System.Text.Json (built-in)
  - Serilog 3.x (optional reference)

Tests at shared/KSquare.PiiRedaction.Tests/:
  - Email field by name → redacted
  - Phone field by name → redacted
  - Non-PII field → unchanged
  - Email value detected by regex (field name = "contactInfo") → redacted
  - SSN detected by regex → redacted
  - Nested JSON object PII field → redacted
  - Array with PII objects → all redacted
  - Invalid JSON input → returned unchanged, no exception
  - Custom redaction token appears in output
```
