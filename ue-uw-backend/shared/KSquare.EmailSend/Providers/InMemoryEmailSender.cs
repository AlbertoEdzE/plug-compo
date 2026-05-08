using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Models;

namespace KSquare.EmailSend.Providers;

public sealed class InMemoryEmailSender : EmailSenderBase
{
    public InMemoryEmailSender(EmailSendOptions options, IEmailTemplateRenderer templates, IServiceProvider services)
        : base(options, templates, services)
    {
    }

    public List<EmailMessage> SentMessages { get; } = new();

    public override Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var normalized = NormalizeMessage(message);
        SentMessages.Add(normalized);

        return Task.FromResult(new EmailSendResult(
            true,
            ProviderMessageId: Guid.NewGuid().ToString("N"),
            Error: null,
            SentAt: DateTimeOffset.UtcNow
        ));
    }
}
