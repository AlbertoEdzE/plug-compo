namespace KSquare.EmailIngestion.Configuration;

public class EmailIngestionOptions
{
    public EmailIngestionProvider Provider { get; set; } = EmailIngestionProvider.MicrosoftGraph;
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? MailboxAddress { get; set; }
    public string? InboxFolderName { get; set; } = "Inbox";
    public string? ProcessedFolderName { get; set; } = "Processed";
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxEmailsPerBatch { get; set; } = 20;
    public string AttachmentContainerName { get; set; } = "email-attachments";
    public string AttachmentPathTemplate { get; set; } = "incoming/{year}/{month}/{day}/{correlationId}/{fileName}";
    public string EventTopic { get; set; } = "email-intake-events";
    public TimeSpan DuplicateDetectionWindow { get; set; } = TimeSpan.FromDays(3);
}

public enum EmailIngestionProvider
{
    MicrosoftGraph,
    Imap,
    Webhook
}
