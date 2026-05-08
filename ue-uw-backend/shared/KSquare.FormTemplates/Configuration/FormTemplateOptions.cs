namespace KSquare.FormTemplates.Configuration;

public sealed class FormTemplateOptions
{
    public FormTemplateProvider Provider { get; set; } = FormTemplateProvider.ITextPdfFill;

    public string? GhostDraftApiUrl { get; set; }
    public string? GhostDraftApiKey { get; set; }
    public string? GhostDraftEnvironment { get; set; } = "production";

    public IDictionary<string, string> GhostDraftTemplateIdMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["acord125"] = "acord125-2024-q1",
        ["quote-proposal"] = "quote-proposal-2024-q1",
        ["binder"] = "binder-2024-q1"
    };

    public string TemplateBlobContainer { get; set; } = "form-templates";

    public string OutputBlobContainer { get; set; } = "generated-forms";
    public string OutputPathTemplate { get; set; } = "forms/{year}/{month}/{resourceId}/{templateName}-{timestamp}.pdf";
    public TimeSpan OutputSasTtl { get; set; } = TimeSpan.FromHours(4);

    public bool StrictRequiredFieldValidation { get; set; } = false;
}

public enum FormTemplateProvider
{
    GhostDraft,
    ITextPdfFill,
    Liquid,
    Mock
}

