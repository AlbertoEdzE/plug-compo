# Component 15 — Form Template Engine

**Library**: `KSquare.FormTemplates`  
**Layer**: Intelligence / Documents  
**Default Provider**: GhostDraft (PDF population) + Fluid (HTML/Liquid) for internal previews  
**Alternate Providers**: DocuSign Templates, iText PDF fill, Azure Logic Apps  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

The UW workbench must populate standard insurance forms with submission and quote data:
- ACORD 125 (Commercial Lines Application) — pre-fill from extracted data for broker review
- NBI Custom Application Forms — populate from submission data model
- Quote Proposal PDF — fill coverage terms, premiums, effective dates
- Binder Document — fill binding confirmation details
- Renewal Notice — pre-fill from expiring policy data

Each of these forms has:
- Fixed template files (PDF forms or DOCX templates) stored in Blob Storage
- A mapping from domain model fields → template placeholder names
- Validation rules (required placeholders must be filled before output)
- A rendering step that produces a final PDF/DOCX binary output
- An output storage step (store to Blob, return SAS URL)

Without a shared library:
- Every service calls GhostDraft differently
- Template placeholder mappings are scattered across services
- Generating a form preview requires duplicating rendering logic

---

## Interface Contract

```csharp
namespace KSquare.FormTemplates.Contracts;

public interface IFormTemplateEngine
{
    // List available templates.
    Task<IReadOnlyList<FormTemplateDescriptor>> ListTemplatesAsync(CancellationToken ct = default);

    // Populate a named template with field values, return binary output (PDF/DOCX).
    Task<FormRenderResult> RenderAsync(FormRenderRequest request, CancellationToken ct = default);

    // Render and store to Blob Storage, return the blob path and SAS URL.
    Task<FormRenderAndStoreResult> RenderAndStoreAsync(FormRenderRequest request, CancellationToken ct = default);
}

public interface IFormFieldMapper
{
    // Map a domain object to a flat dictionary of template placeholder → value.
    IDictionary<string, string?> MapFields<TSource>(string templateName, TSource source) where TSource : class;
}
```

---

## Models

```csharp
namespace KSquare.FormTemplates.Models;

public record FormRenderRequest
{
    public required string TemplateName { get; init; }         // "acord125", "quote-proposal", "binder"
    public required IDictionary<string, string?> Fields { get; init; }
    public string? OutputFormat { get; init; } = "pdf";       // "pdf", "docx", "html"
    public string? CorrelationId { get; init; }
    public string? RelatedResourceId { get; init; }            // submission/quote ID for naming output
}

public record FormRenderResult
{
    public required byte[] Content { get; init; }
    public required string ContentType { get; init; }          // "application/pdf"
    public required string FileName { get; init; }
    public required string TemplateName { get; init; }
    public required string TemplateVersion { get; init; }
    public required IReadOnlyList<string> UnfilledRequiredFields { get; init; }
    public bool IsComplete => !UnfilledRequiredFields.Any();
    public DateTimeOffset RenderedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record FormRenderAndStoreResult(
    FormRenderResult RenderResult,
    string BlobPath,
    string SasUrl,
    DateTimeOffset SasExpiry
);

public record FormTemplateDescriptor
{
    public required string TemplateName { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string OutputFormat { get; init; }
    public required IReadOnlyList<FormFieldDescriptor> Fields { get; init; }
}

public record FormFieldDescriptor(
    string PlaceholderName,
    string DisplayLabel,
    bool Required,
    string FieldType   // "text", "date", "decimal", "boolean", "multiline"
);
```

---

## Configuration

```csharp
public class FormTemplateOptions
{
    public FormTemplateProvider Provider { get; set; } = FormTemplateProvider.ITextPdfFill;

    // GhostDraft (cloud rendering API)
    public string? GhostDraftApiUrl { get; set; }
    public string? GhostDraftApiKey { get; set; }
    public string? GhostDraftEnvironment { get; set; } = "production";

    // Template storage (Blob)
    public string TemplateBlobContainer { get; set; } = "form-templates";

    // Output storage (Blob)
    public string OutputBlobContainer { get; set; } = "generated-forms";
    public string OutputPathTemplate { get; set; } = "forms/{year}/{month}/{resourceId}/{templateName}-{timestamp}.pdf";
    public TimeSpan OutputSasTtl { get; set; } = TimeSpan.FromHours(4);

    // Validation
    public bool StrictRequiredFieldValidation { get; set; } = false;
}

public enum FormTemplateProvider
{
    GhostDraft,         // cloud-hosted PDF template engine
    ITextPdfFill,       // iText7 AcroForm PDF fill (self-hosted)
    Liquid,             // Fluid HTML template for previews
    Mock                // returns empty PDF bytes for tests
}
```

---

## DI Registration

```csharp
builder.Services.AddKsFormTemplates(options =>
{
    builder.Configuration.GetSection("KSquare:FormTemplates").Bind(options);
    options.GhostDraftApiKey = builder.Configuration["GhostDraft--ApiKey"];
    options.Provider = FormTemplateProvider.GhostDraft;
})
// Requires KSquare.BlobStorage for template loading and output storage.
;
```

---

## Template Field Mapping Definitions

Each template has a YAML field map defining how domain model properties map to template placeholders:

```yaml
# templates/acord125-field-map.yml
template_name: acord125
version: "2024-Q1"
output_format: pdf
display_name: ACORD 125 Commercial Lines Application
fields:
  - placeholder: NamedInsured
    domain_path: InsuredName
    required: true
    type: text

  - placeholder: MailingAddress
    domain_path: InsuredAddress
    required: true
    type: text

  - placeholder: EffectiveDate
    domain_path: PolicyEffectiveDate
    format: MM/dd/yyyy
    required: true
    type: date

  - placeholder: ExpirationDate
    domain_path: PolicyExpirationDate
    format: MM/dd/yyyy
    required: true
    type: date

  - placeholder: TotalInsuredValue
    domain_path: TotalInsuredValue
    format: "C0"         # currency, no decimal
    required: false
    type: decimal

  - placeholder: BrokerName
    domain_path: BrokerName
    required: false
    type: text

  - placeholder: BrokerLicenseNo
    domain_path: BrokerLicenseNumber
    required: false
    type: text
```

```yaml
# templates/quote-proposal-field-map.yml
template_name: quote-proposal
version: "2024-Q1"
display_name: Quote Proposal
output_format: pdf
fields:
  - placeholder: InsuredName
    domain_path: InsuredName
    required: true
    type: text

  - placeholder: QuoteNumber
    domain_path: QuoteNumber
    required: true
    type: text

  - placeholder: EffectiveDate
    domain_path: EffectiveDate
    format: MMMM dd, yyyy
    required: true
    type: date

  - placeholder: ExpirationDate
    domain_path: ExpirationDate
    format: MMMM dd, yyyy
    required: true
    type: date

  - placeholder: TotalPremium
    domain_path: TotalPremium
    format: "C2"
    required: true
    type: decimal

  - placeholder: BrokerName
    domain_path: BrokerName
    required: true
    type: text
```

---

## GhostDraft Provider Implementation

```csharp
public class GhostDraftFormTemplateEngine : IFormTemplateEngine
{
    // POST to GhostDraft API: { templateId, fields: { key: value }, outputFormat }
    // GhostDraft returns binary PDF in response body
    public async Task<FormRenderResult> RenderAsync(FormRenderRequest request, CancellationToken ct)
    {
        var ghostDraftRequest = new
        {
            templateId = MapTemplateNameToGhostDraftId(request.TemplateName),
            fields = request.Fields,
            outputFormat = request.OutputFormat ?? "pdf"
        };

        var response = await _httpClient.PostAsJsonAsync("/api/v1/render", ghostDraftRequest, ct);
        response.EnsureSuccessStatusCode();

        var pdfBytes = await response.Content.ReadAsByteArrayAsync(ct);
        var templateInfo = await GetTemplateDescriptorAsync(request.TemplateName, ct);
        var unfilledRequired = templateInfo.Fields
            .Where(f => f.Required && (!request.Fields.ContainsKey(f.PlaceholderName) || string.IsNullOrEmpty(request.Fields[f.PlaceholderName])))
            .Select(f => f.PlaceholderName)
            .ToList();

        return new FormRenderResult
        {
            Content = pdfBytes,
            ContentType = "application/pdf",
            FileName = $"{request.TemplateName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.pdf",
            TemplateName = request.TemplateName,
            TemplateVersion = templateInfo.Version,
            UnfilledRequiredFields = unfilledRequired
        };
    }
}
```

---

## iText AcroForm Provider (Self-Hosted Fallback)

```csharp
public class ITextPdfFormEngine : IFormTemplateEngine
{
    // Uses iText7 to fill PDF AcroForm fields
    public async Task<FormRenderResult> RenderAsync(FormRenderRequest request, CancellationToken ct)
    {
        // Load template PDF from blob storage
        var templateBlob = await _blobStorage.DownloadAsync(_options.TemplateBlobContainer, $"{request.TemplateName}.pdf", ct);

        using var ms = new MemoryStream();
        using var reader = new PdfReader(templateBlob.Stream);
        using var writer = new PdfWriter(ms);
        using var pdf = new PdfDocument(reader, writer);
        var form = PdfAcroForm.GetAcroForm(pdf, true);

        foreach (var (key, value) in request.Fields)
        {
            var field = form.GetField(key);
            if (field != null && value != null)
                field.SetValue(value);
        }

        form.FlattenFields();   // flatten to prevent further editing
        pdf.Close();

        return new FormRenderResult
        {
            Content = ms.ToArray(),
            ContentType = "application/pdf",
            FileName = $"{request.TemplateName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.pdf",
            TemplateName = request.TemplateName,
            TemplateVersion = "1.0",
            UnfilledRequiredFields = []
        };
    }
}
```

---

## Usage Example

```csharp
// Map submission domain model to form fields
var fields = formFieldMapper.MapFields("acord125", new
{
    InsuredName = submission.InsuredName,
    InsuredAddress = submission.InsuredAddress,
    PolicyEffectiveDate = submission.EffectiveDate,
    PolicyExpirationDate = submission.ExpirationDate,
    TotalInsuredValue = submission.TotalInsuredValue,
    BrokerName = submission.Broker.DisplayName,
    BrokerLicenseNumber = submission.Broker.LicenseNumber
});

// Render and store
var result = await formEngine.RenderAndStoreAsync(new FormRenderRequest
{
    TemplateName = "acord125",
    Fields = fields,
    CorrelationId = correlationId,
    RelatedResourceId = submission.SubmissionId.ToString()
});

if (!result.RenderResult.IsComplete)
{
    log.LogWarning("Form has unfilled required fields: {Fields}",
        string.Join(", ", result.RenderResult.UnfilledRequiredFields));
}

// Return SAS URL for download
return new { documentUrl = result.SasUrl, expiresAt = result.SasExpiry };
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| GhostDraft API unavailable | Polly retry 3x; throw `FormRenderException` after exhaustion |
| Template file not found in blob | Throw `FormTemplateNotFoundException` with template name |
| Required field not filled | Return result with `UnfilledRequiredFields` populated; only throw if `StrictMode = true` |
| iText PDF read fails (corrupt template) | Throw `FormTemplateCorruptException`; alert ops |
| Output blob store fails | Do not return result; throw; caller decides retry |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.FormTemplates at path: shared/KSquare.FormTemplates/

This library populates insurance form templates (ACORD 125, Quote Proposal, Binder) with
domain model data. It uses YAML field maps to translate domain property names to template
placeholder names. Primary output is PDF (via GhostDraft or iText7 AcroForm).

Project structure:
  shared/KSquare.FormTemplates/
  ├── KSquare.FormTemplates.csproj
  ├── Contracts/
  │   ├── IFormTemplateEngine.cs
  │   └── IFormFieldMapper.cs
  ├── Models/
  │   ├── FormRenderRequest.cs
  │   ├── FormRenderResult.cs
  │   ├── FormRenderAndStoreResult.cs
  │   ├── FormTemplateDescriptor.cs
  │   └── FormFieldDescriptor.cs
  ├── Configuration/
  │   └── FormTemplateOptions.cs
  ├── FieldMaps/
  │   ├── FieldMapLoader.cs              ← loads YAML field maps from embedded resources or blob
  │   └── Resources/
  │       ├── acord125-field-map.yml
  │       ├── quote-proposal-field-map.yml
  │       └── binder-field-map.yml
  ├── Providers/
  │   ├── GhostDraftFormEngine.cs        ← HTTP API call to GhostDraft
  │   ├── ITextPdfFormEngine.cs          ← iText7 AcroForm fill
  │   └── MockFormEngine.cs             ← returns minimal valid PDF bytes for tests
  ├── Internal/
  │   └── ReflectionFieldMapper.cs       ← walks domain object by domain_path using reflection
  └── Extensions/
      └── ServiceCollectionExtensions.cs

ReflectionFieldMapper.MapFields<TSource>:
  1. Load field map YAML for templateName via FieldMapLoader
  2. For each field descriptor in map:
     a. Read domain_path from source object using reflection (support dot-notation: "Broker.DisplayName")
     b. Format value according to field type (date → format string, decimal → format string)
     c. Add to result dictionary as {placeholder: formattedValue}
  3. Return IDictionary<string, string?>

FieldMapLoader:
  - Use YamlDotNet to deserialize field map YAML files
  - Cache in ConcurrentDictionary keyed by template name
  - Support both embedded resource loading and Blob Storage loading

GhostDraftFormEngine:
  - Use IHttpClientFactory to get named client "ghostdraft"
  - POST JSON body: { templateId, fields, outputFormat }
  - Parse binary PDF from response
  - Retry 3x on 5xx with Polly exponential backoff
  - Map templateName → GhostDraft templateId via configurable dictionary

ITextPdfFormEngine:
  - Download template PDF from Blob Storage using IBlobStorageConnector
  - Use iText7 PdfAcroForm to fill fields
  - Flatten form fields to produce read-only output PDF
  - Return filled PDF bytes

RenderAndStoreAsync (base class or extension):
  - Call RenderAsync
  - Upload result to output blob using IBlobStorageConnector with path from OutputPathTemplate
  - Generate SAS URL with OutputSasTtl via IBlobStorageConnector.GenerateSasUrlAsync
  - Return FormRenderAndStoreResult

Include these 3 YAML field map files as embedded resources with the field definitions
shown in the spec (at least 5 fields each):
  - acord125-field-map.yml
  - quote-proposal-field-map.yml
  - binder-field-map.yml

NuGet packages:
  - itext7 7.x (for ITextPdfFormEngine)
  - YamlDotNet 13.x
  - Microsoft.Extensions.Http
  - Polly 8.x

Tests at shared/KSquare.FormTemplates.Tests/ (use Mock provider):
  - MapFields maps InsuredName from source object to NamedInsured placeholder
  - MapFields applies date format MM/dd/yyyy to date field
  - MapFields applies currency format to decimal field
  - MapFields returns null for missing optional field (no exception)
  - RenderAsync with Mock provider returns non-empty byte array
  - UnfilledRequiredFields lists field when required field value is null
  - IsComplete is false when UnfilledRequiredFields is non-empty
  Use xUnit + FluentAssertions.
```
