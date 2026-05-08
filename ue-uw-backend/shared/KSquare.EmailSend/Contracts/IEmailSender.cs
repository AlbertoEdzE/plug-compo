using KSquare.EmailSend.Models;

namespace KSquare.EmailSend.Contracts;

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);

    Task<EmailSendResult> SendTemplatedAsync<TModel>(
        string templateName,
        TModel model,
        EmailAddress to,
        string? subject = null,
        IReadOnlyList<EmailAttachmentRef>? attachments = null,
        CancellationToken ct = default
    )
        where TModel : class;
}
