namespace KSquare.EmailSend.Configuration;

public class EmailSendOptions
{
    public EmailSendProvider Provider { get; set; } = EmailSendProvider.SendGrid;
    public string? SendGridApiKey { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;
    public string DefaultFromAddress { get; set; } = "noreply@company.com";
    public string DefaultFromName { get; set; } = "UW Workbench";
    public EmailTemplateSource TemplateSource { get; set; } = EmailTemplateSource.EmbeddedResource;
    public string? TemplateBlobContainerName { get; set; } = "email-templates";
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}

public enum EmailSendProvider
{
    SendGrid,
    Smtp,
    AzureCommunicationServices,
    InMemory
}

public enum EmailTemplateSource
{
    EmbeddedResource,
    BlobStorage,
    FileSystem
}
