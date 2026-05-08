using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bogus;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Extensions;
using KSquare.BlobStorage.Models;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Extensions;
using KSquare.Correlation.Models;
using KSquare.DocumentClassification.Configuration;
using KSquare.DocumentClassification.Contracts;
using KSquare.DocumentClassification.Extensions;
using KSquare.DocumentExtraction.Configuration;
using KSquare.DocumentExtraction.Contracts;
using KSquare.DocumentExtraction.Extensions;
using KSquare.DocumentExtraction.Models;
using KSquare.ExtractionMapper.Configuration;
using KSquare.ExtractionMapper.Contracts;
using KSquare.ExtractionMapper.Extensions;
using KSquare.RiskAnalysis.Configuration;
using KSquare.RiskAnalysis.Contracts;
using KSquare.RiskAnalysis.Extensions;
using KSquare.RiskAnalysis.Models;
using KSquare.RulesEngine.Configuration;
using KSquare.RulesEngine.Context;
using KSquare.RulesEngine.Contracts;
using KSquare.RulesEngine.Extensions;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Contracts;
using KSquare.FormTemplates.Extensions;
using Microsoft.Extensions.DependencyInjection;

var assertions = new List<AssertionRecord>();

var seedRaw = Environment.GetEnvironmentVariable("CANVAS_SEED") ?? "42";
var seed = int.TryParse(seedRaw, out var parsedSeed) ? parsedSeed : 42;
Randomizer.Seed = new Random(seed);
var faker = new Faker();

var submissionId = $"SUB-{faker.Random.Int(1000, 9999)}";
var correlationId = faker.Random.Guid().ToString("N");

var synthJson = Environment.GetEnvironmentVariable("CANVAS3_SYNTH_JSON") ?? "";
Canvas3Synth? synth = null;
try
{
    var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    synth = JsonSerializer.Deserialize<Canvas3Synth>(synthJson, opts);
}
catch
{
    synth = null;
}

Record(
    "synthesize_canvas3_inputs",
    synth is not null && !string.IsNullOrWhiteSpace(submissionId) && !string.IsNullOrWhiteSpace(correlationId),
    $"seed={seed} submissionId={submissionId} correlationId={correlationId}"
);

var wiremock = Environment.GetEnvironmentVariable("LAB_WIREMOCK") ?? "http://localhost:8080";
var azurite = Environment.GetEnvironmentVariable("LAB_AZURITE_BLOB")
              ?? "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey="
              + "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
              + "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

await Run("wiremock_stub_document_intelligence", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var acord = synth.Acord125;

    var extractedFields = new[]
    {
        new { name = "NamedInsured", value = acord.InsuredName, confidence = 0.95f, boundingBox = (object?)null, pageNumber = 1 },
        new { name = "EffectiveDate", value = acord.PolicyEffectiveDate, confidence = 0.92f, boundingBox = (object?)null, pageNumber = 1 },
        new { name = "TIV", value = $"${acord.TotalInsuredValueUsd:N0}", confidence = 0.91f, boundingBox = (object?)null, pageNumber = 1 },
        new { name = "NumberOfLocations", value = acord.NumberOfLocations.ToString(CultureInfo.InvariantCulture), confidence = 0.88f, boundingBox = (object?)null, pageNumber = 1 },
        new { name = "UnmappedField", value = faker.Lorem.Word(), confidence = 0.99f, boundingBox = (object?)null, pageNumber = 1 },
    };

    var table = new
    {
        tableName = "loss_run",
        pageNumber = 1,
        headers = new[] { "Year", "Claims", "Incurred", "Loss Ratio" },
        rows = acord.LossRunRows
            .Select(r => (IReadOnlyList<string?>)new string?[]
            {
                r.Year.ToString(CultureInfo.InvariantCulture),
                r.ClaimsCount.ToString(CultureInfo.InvariantCulture),
                $"${r.IncurredUsd:N0}",
                $"{r.LossRatioPercent}%"
            })
            .ToArray(),
        confidence = 0.95f
    };

    var extractionResult = new
    {
        documentId = $"{submissionId}-acord125",
        providerOperationId = $"wiremock-op-{submissionId}",
        status = "Succeeded",
        fields = extractedFields,
        tables = new[] { table },
        pages = new[] { new { pageNumber = 1, width = 1000, height = 1000, unit = "pixel" } },
        detectedDocumentType = "ACORD125",
        overallConfidence = 0.93f,
        extractedAt = DateTimeOffset.UtcNow,
        modelUsed = "wiremock",
        correlationId = correlationId
    };

    var classifyResult = new
    {
        documentType = "ACORD125",
        confidence = 0.93f,
        method = "AzureDocumentClassifier",
        alternativeCandidates = Array.Empty<object>(),
        correlationId = correlationId,
        classifiedAt = DateTimeOffset.UtcNow
    };

    using var http = new HttpClient { BaseAddress = new Uri(wiremock) };
    await http.PostAsync("/__admin/reset", content: null);

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "POST", urlPath = "/api/extract" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = extractionResult
        }
    });

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "POST", urlPath = "/api/classify" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = classifyResult
        }
    });
});

await Run("document_extraction_confidence_routing_autoaccepted", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();

    var corr = sp.GetRequiredService<ICorrelationContextAccessor>();
    corr.Current = new CorrelationContext(correlationId, "tenant-1", "uw-1");

    var blob = sp.GetRequiredService<IBlobStorageConnector>();
    var inputBlobPath = await UploadDocumentAsync(blob, synth.Acord125.Text, submissionId);

    var extractor = sp.GetRequiredService<IDocumentExtractor>();

    var result = await extractor.ExtractAsync(new DocumentInput
    {
        BlobPath = inputBlobPath,
        ContentType = "application/pdf",
        FileName = "acord125.pdf"
    }, modelHint: "ACORD125");

    var options = sp.GetRequiredService<DocumentExtractionOptions>();
    var field = result.Fields.FirstOrDefault(f => f.Name.Equals("NamedInsured", StringComparison.OrdinalIgnoreCase));
    if (field is null)
    {
        throw new InvalidOperationException("Expected extracted field 'NamedInsured'.");
    }
    if (field.Confidence < options.AutoAcceptThreshold)
    {
        throw new InvalidOperationException($"Expected AutoAccepted threshold met (>= {options.AutoAcceptThreshold}), got {field.Confidence}.");
    }
    if (result.Status == ExtractionStatus.PendingReview)
    {
        throw new InvalidOperationException("Expected ExtractionStatus not PendingReview for synthesized high-confidence fields.");
    }
});

await Run("document_classification_acord125", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();

    var corr = sp.GetRequiredService<ICorrelationContextAccessor>();
    corr.Current = new CorrelationContext(correlationId, "tenant-1", "uw-1");

    var blob = sp.GetRequiredService<IBlobStorageConnector>();
    var inputBlobPath = await UploadDocumentAsync(blob, synth.Acord125.Text, submissionId);

    var classifier = sp.GetRequiredService<IDocumentClassifier>();
    var result = await classifier.ClassifyAsync(new KSquare.DocumentClassification.Models.DocumentInput
    {
        BlobPath = inputBlobPath,
        ContentType = "application/pdf",
        FileName = "acord125.pdf",
        FirstPageText = synth.Acord125.Text.Length > 512 ? synth.Acord125.Text[..512] : synth.Acord125.Text
    });

    if (!result.DocumentType.Equals("ACORD125", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected ACORD125, got {result.DocumentType}.");
    }
});

await Run("extraction_mapper_maps_required_fields_and_collects_unmapped", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();

    var corr = sp.GetRequiredService<ICorrelationContextAccessor>();
    corr.Current = new CorrelationContext(correlationId, "tenant-1", "uw-1");

    var blob = sp.GetRequiredService<IBlobStorageConnector>();
    var inputBlobPath = await UploadDocumentAsync(blob, synth.Acord125.Text, submissionId);

    var extractor = sp.GetRequiredService<IDocumentExtractor>();
    var extraction = await extractor.ExtractAsync(new DocumentInput
    {
        BlobPath = inputBlobPath,
        ContentType = "application/pdf",
        FileName = "acord125.pdf"
    }, modelHint: "ACORD125");

    var mapper = sp.GetRequiredService<IExtractionMapper>();
    var mapped = mapper.MapToDictionary(extraction, "ACORD125");
    if (!mapped.Value.TryGetValue("InsuredName", out var insuredObj) || insuredObj is not string insuredName)
    {
        throw new InvalidOperationException("Expected mapped InsuredName.");
    }
    if (!insuredName.Equals(synth.Acord125.InsuredName, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Mapped InsuredName mismatch.");
    }

    if (!mapped.Value.TryGetValue("PolicyEffectiveDate", out var effObj) || effObj is not DateOnly effDate)
    {
        throw new InvalidOperationException("Expected mapped PolicyEffectiveDate as DateOnly.");
    }

    var expectedEff = DateOnly.ParseExact(synth.Acord125.PolicyEffectiveDate, "MM/dd/yyyy", CultureInfo.InvariantCulture);
    if (effDate != expectedEff)
    {
        throw new InvalidOperationException($"Mapped PolicyEffectiveDate mismatch. expected={expectedEff} actual={effDate}");
    }

    var mappedSourceNames = new HashSet<string>(
        mapped.MappedFields
            .Select(f => f.SourceFieldName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!),
        StringComparer.OrdinalIgnoreCase
    );

    var unmapped = extraction.Fields
        .Select(f => f.Name)
        .Where(n => !mappedSourceNames.Contains(n))
        .ToList();

    if (!unmapped.Contains("UnmappedField", StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Expected at least one unmapped field for downstream IntelligentPrefill.");
    }
});

await Run("rules_intake_routing_routes_to_senior_underwriter", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();
    var rules = sp.GetRequiredService<IRulesEngine>();

    var ctx = new IntakeRoutingContext
    {
        TotalInsuredValue = synth.Acord125.TotalInsuredValueUsd,
        BrokerTenureMonths = 24,
        NaicsCode = synth.Acord125.NaicsCode,
        MissingRequiredFields = Array.Empty<string>(),
        NumberOfLocations = synth.Acord125.NumberOfLocations,
        SubmissionSource = "email"
    };

    var action = await rules.GetFirstMatchedActionAsync("intake-routing", ctx);
    if (!string.Equals(action, "RouteToSeniorUnderwriter", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected RouteToSeniorUnderwriter, got {action ?? "<null>"}.");
    }
});

await Run("risk_analysis_composite_score_matches_hand_calculated", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();

    var corr = sp.GetRequiredService<ICorrelationContextAccessor>();
    corr.Current = new CorrelationContext(correlationId, "tenant-1", "uw-1");

    var blob = sp.GetRequiredService<IBlobStorageConnector>();
    var inputBlobPath = await UploadDocumentAsync(blob, synth.Acord125.Text, submissionId);

    var extractor = sp.GetRequiredService<IDocumentExtractor>();
    var extraction = await extractor.ExtractAsync(new DocumentInput
    {
        BlobPath = inputBlobPath,
        ContentType = "application/pdf",
        FileName = "acord125.pdf"
    }, modelHint: "ACORD125");

    var engine = sp.GetRequiredService<IRiskAnalysisEngine>();
    var request = new RiskAnalysisRequest
    {
        SubmissionId = submissionId,
        InstitutionType = synth.Risk.InstitutionType,
        NaicsCode = synth.Acord125.NaicsCode,
        NumberOfLocations = synth.Acord125.NumberOfLocations,
        TotalInsuredValue = synth.Acord125.TotalInsuredValueUsd,
        NumberOfCoverageLines = synth.Acord125.CoverageLines.Count,
        CoverageLineNames = synth.Acord125.CoverageLines,
        FormResponses = synth.Risk.FormResponses.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value),
        LossRunTables = extraction.Tables
    };

    var result = await engine.AnalyzeAsync(request);
    var scores = result.RiskIndicators;

    var expected = (decimal)scores.CampusSafetyRating.Score * 0.30m
                   + (100m - scores.ClaimsSeverity.Score) * 0.30m
                   + (100m - scores.PolicyComplexity.Score) * 0.20m
                   + (100m - scores.LitigationExposure.Score) * 0.20m;

    var diff = Math.Abs(scores.CompositeRiskScore - expected);
    if (diff > 0.000001m)
    {
        throw new InvalidOperationException($"CompositeRiskScore mismatch. expected={expected} actual={scores.CompositeRiskScore} diff={diff}");
    }
});

await Run("risk_analysis_appetite_fit_classification", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();

    var corr = sp.GetRequiredService<ICorrelationContextAccessor>();
    corr.Current = new CorrelationContext(correlationId, "tenant-1", "uw-1");

    var blob = sp.GetRequiredService<IBlobStorageConnector>();
    var inputBlobPath = await UploadDocumentAsync(blob, synth.Acord125.Text, submissionId);

    var extractor = sp.GetRequiredService<IDocumentExtractor>();
    var extraction = await extractor.ExtractAsync(new DocumentInput
    {
        BlobPath = inputBlobPath,
        ContentType = "application/pdf",
        FileName = "acord125.pdf"
    }, modelHint: "ACORD125");

    var engine = sp.GetRequiredService<IRiskAnalysisEngine>();
    var request = new RiskAnalysisRequest
    {
        SubmissionId = submissionId,
        InstitutionType = synth.Risk.InstitutionType,
        NaicsCode = synth.Acord125.NaicsCode,
        NumberOfLocations = synth.Acord125.NumberOfLocations,
        TotalInsuredValue = synth.Acord125.TotalInsuredValueUsd,
        NumberOfCoverageLines = synth.Acord125.CoverageLines.Count,
        CoverageLineNames = synth.Acord125.CoverageLines,
        FormResponses = synth.Risk.FormResponses.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value),
        LossRunTables = extraction.Tables
    };

    var result = await engine.AnalyzeAsync(request);
    var score = result.AppetiteFit.Score;
    var expected = score >= 0.80f ? "In Appetite" : score >= 0.60f ? "Borderline" : "Out of Appetite";

    if (!string.Equals(result.AppetiteFit.Classification, expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Appetite classification mismatch. expected={expected} actual={result.AppetiteFit.Classification} score={score}");
    }
});

await Run("rules_bind_readiness_ready_and_not_ready", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS3_SYNTH_JSON.");
    }

    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();

    var rules = sp.GetRequiredService<IRulesEngine>();

    var complete = new BindReadinessContext
    {
        QuoteStatus = synth.Risk.BindReadiness.Complete.QuoteStatus,
        HasSignedApplication = synth.Risk.BindReadiness.Complete.HasSignedApplication,
        PremiumAgreedByBroker = synth.Risk.BindReadiness.Complete.PremiumAgreedByBroker,
        ComplianceCheckPassed = synth.Risk.BindReadiness.Complete.ComplianceCheckPassed,
        ReferralApproved = synth.Risk.BindReadiness.Complete.ReferralApproved,
    };

    var incomplete = new BindReadinessContext
    {
        QuoteStatus = synth.Risk.BindReadiness.Incomplete.QuoteStatus,
        HasSignedApplication = synth.Risk.BindReadiness.Incomplete.HasSignedApplication,
        PremiumAgreedByBroker = synth.Risk.BindReadiness.Incomplete.PremiumAgreedByBroker,
        ComplianceCheckPassed = synth.Risk.BindReadiness.Incomplete.ComplianceCheckPassed,
        ReferralApproved = synth.Risk.BindReadiness.Incomplete.ReferralApproved,
    };

    var completeAction = await rules.GetFirstMatchedActionAsync("bind-readiness", complete);
    if (!string.Equals(completeAction, "AllowBind", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected Ready/AllowBind, got {completeAction ?? "<null>"}.");
    }

    var incompleteEval = await rules.EvaluateAsync("bind-readiness", incomplete);
    if (!incompleteEval.FiredActions.Contains("BlockBind", StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Expected NotReady/BlockBind for missing required field.");
    }

    var fired = incompleteEval.Results.Where(r => r.Fired).ToList();
    var hasSignedAppRule = fired.Any(r => r.RuleName.Equals("SignedApplicationMissing", StringComparison.OrdinalIgnoreCase));
    var mentionsSigned = fired.Any(r => (r.Reason ?? "").Contains("Signed application", StringComparison.OrdinalIgnoreCase));
    if (!hasSignedAppRule || !mentionsSigned)
    {
        throw new InvalidOperationException("Expected blocking reason to name missing signed application.");
    }
});

await Run("form_templates_itext_renders_non_empty_pdf", async () =>
{
    var services = BuildServices(wiremock, azurite);
    using var sp = services.BuildServiceProvider();

    var corr = sp.GetRequiredService<ICorrelationContextAccessor>();
    corr.Current = new CorrelationContext(correlationId, "tenant-1", "uw-1");

    var blob = sp.GetRequiredService<IBlobStorageConnector>();

    var engine = sp.GetRequiredService<IFormTemplateEngine>();
    var templates = await engine.ListTemplatesAsync();
    var template = templates.FirstOrDefault(t => t.TemplateName.Equals("acord125", StringComparison.OrdinalIgnoreCase))
                   ?? templates.FirstOrDefault();
    if (template is null)
    {
        throw new InvalidOperationException("No form templates available.");
    }

    var fieldName = template.Fields.FirstOrDefault()?.PlaceholderName;
    if (string.IsNullOrWhiteSpace(fieldName))
    {
        throw new InvalidOperationException("Form template has no fields.");
    }

    var templateBytes = BuildSingleFieldPdf(fieldName);
    await using (var ms = new MemoryStream(templateBytes))
    {
        await blob.UploadAsync(new BlobUploadRequest("form-templates", $"{template.TemplateName}.pdf", ms, "application/pdf"));
    }

    var render = await engine.RenderAsync(new KSquare.FormTemplates.Models.FormRenderRequest
    {
        TemplateName = template.TemplateName,
        Fields = new Dictionary<string, string?> { [fieldName] = faker.Company.CompanyName() },
        OutputFormat = "pdf",
        RelatedResourceId = submissionId
    });

    if (render.Content.Length == 0)
    {
        throw new InvalidOperationException("Expected non-empty PDF bytes.");
    }
});

var output = JsonSerializer.Serialize(new { assertions }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
Console.WriteLine(output);

void Record(string name, bool passed, string details) => assertions.Add(new AssertionRecord(name, passed, details));

async Task Run(string name, Func<Task> fn)
{
    try
    {
        await fn();
        Record(name, true, "");
    }
    catch (Exception ex)
    {
        Record(name, false, ex.Message);
    }
}

static IServiceCollection BuildServices(string wiremockBaseUrl, string azuriteConnectionString)
{
    var services = new ServiceCollection();
    services.AddLogging();

    services.AddKsCorrelation();

    services.AddKsBlobStorage(opt =>
    {
        opt.Provider = BlobProvider.Azure;
        opt.ConnectionString = azuriteConnectionString;
    });

    services.AddKsDocumentExtraction(opt =>
    {
        opt.Provider = DocumentExtractionProvider.AzureDocumentIntelligence;
        opt.FunctionBaseUrl = wiremockBaseUrl;
        opt.LowConfidenceThreshold = 0.75f;
        opt.AutoAcceptThreshold = 0.90f;
    });

    services.AddKsDocumentClassification(opt =>
    {
        opt.Provider = DocumentClassificationProvider.AzureOnly;
        opt.FunctionBaseUrl = wiremockBaseUrl;
        opt.SasExpiry = TimeSpan.FromMinutes(30);
    });

    services.AddKsExtractionMapper(opt =>
    {
        opt.RuleSource = MappingRuleSource.EmbeddedYaml;
        opt.StrictMode = false;
    });

    services.AddKsRulesEngine(opt =>
    {
        opt.RuleSource = RuleSetSource.EmbeddedYaml;
        opt.CacheTtl = TimeSpan.FromMinutes(5);
    })
    .AddRuleSet("intake-routing")
    .AddRuleSet("bind-readiness")
    .AddRuleSet("appetite-scoring");

    services.AddKsRiskAnalysis(opt =>
    {
        opt.OutOfAppetiteNaicsCodes = Array.Empty<string>();
        opt.MinimumLossHistoryYears = 3;
    });

    services.AddKsFormTemplates(opt =>
    {
        opt.Provider = FormTemplateProvider.ITextPdfFill;
        opt.TemplateBlobContainer = "form-templates";
        opt.OutputBlobContainer = "generated-forms";
        opt.StrictRequiredFieldValidation = false;
    });

    return services;
}

static async Task<string> UploadDocumentAsync(IBlobStorageConnector blob, string text, string submissionId)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    await using var ms = new MemoryStream(bytes);
    var upload = await blob.UploadAsync(new BlobUploadRequest("documents", $"submissions/{submissionId}/acord125.pdf", ms, "application/pdf"));
    return upload.BlobPath;
}

static byte[] BuildSingleFieldPdf(string fieldName)
{
    using var ms = new MemoryStream();
    using var writer = new PdfWriter(ms);
    using var pdf = new PdfDocument(writer);
    var page = pdf.AddNewPage(PageSize.LETTER);
    var form = PdfAcroForm.GetAcroForm(pdf, true);
    var rect = new Rectangle(50f, 760f, 500f, 18f);
    var field = new TextFormFieldBuilder(pdf, fieldName)
        .SetWidgetRectangle(rect)
        .SetPage(page)
        .CreateText();
    form.AddField(field, page);
    pdf.Close();
    return ms.ToArray();
}

sealed record AssertionRecord(string name, bool passed, string details);

sealed record Canvas3Synth(Canvas3Acord125 Acord125, Canvas3Risk Risk);

sealed record Canvas3Acord125(
    string InsuredName,
    string BrokerName,
    string NaicsCode,
    int TotalInsuredValueUsd,
    int NumberOfLocations,
    string PolicyEffectiveDate,
    List<string> CoverageLines,
    List<Canvas3LossRunRow> LossRunRows,
    string Text
);

sealed record Canvas3LossRunRow(int Year, int ClaimsCount, int IncurredUsd, int LossRatioPercent);

sealed record Canvas3Risk(
    string InstitutionType,
    Dictionary<string, string> FormResponses,
    Canvas3BindReadiness BindReadiness
);

sealed record Canvas3BindReadiness(Canvas3BindReadinessContext Complete, Canvas3BindReadinessContext Incomplete);

sealed record Canvas3BindReadinessContext(
    string QuoteStatus,
    bool HasSignedApplication,
    bool PremiumAgreedByBroker,
    bool ComplianceCheckPassed,
    bool ReferralApproved
);
