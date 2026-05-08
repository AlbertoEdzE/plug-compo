using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Contracts;
using KSquare.Correlation.Models;
using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Exceptions;
using KSquare.EmailSend.Internal;
using KSquare.EmailSend.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.EmailSend.Providers;

public abstract class EmailSenderBase(EmailSendOptions options, IEmailTemplateRenderer templates, IServiceProvider services)
    : IEmailSender
{
    public abstract Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);

    public async Task<EmailSendResult> SendTemplatedAsync<TModel>(
        string templateName,
        TModel model,
        EmailAddress to,
        string? subject = null,
        IReadOnlyList<EmailAttachmentRef>? attachments = null,
        CancellationToken ct = default
    )
        where TModel : class
    {
        var rendered = await templates.RenderAsync(templateName, model, ct);

        var from = new EmailAddress(options.DefaultFromAddress, options.DefaultFromName);
        var msg = new EmailMessage
        {
            From = from,
            To = [to],
            Subject = subject ?? rendered.Subject,
            HtmlBody = rendered.HtmlBody,
            TextBody = rendered.TextBody,
            Attachments = attachments ?? [],
        };

        return await SendAsync(msg, ct);
    }

    protected EmailMessage NormalizeMessage(EmailMessage message)
    {
        var correlationId = message.CorrelationId
            ?? services.GetService<ICorrelationContextAccessor>()?.Current?.CorrelationId;

        var headers = new Dictionary<string, string>(message.Headers, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers[CorrelationHeaders.CorrelationId] = correlationId;
        }

        var textBody = message.TextBody;
        if (textBody is null)
        {
            textBody = HtmlToTextConverter.Convert(message.HtmlBody);
        }

        return message with
        {
            CorrelationId = correlationId,
            Headers = headers,
            TextBody = textBody
        };
    }

    protected async Task<IReadOnlyList<ResolvedAttachment>> ResolveAttachmentsAsync(IReadOnlyList<EmailAttachmentRef> attachments, CancellationToken ct)
    {
        if (attachments.Count == 0)
        {
            return Array.Empty<ResolvedAttachment>();
        }

        var blob = services.GetService<IBlobStorageConnector>();

        var resolved = new List<ResolvedAttachment>(attachments.Count);
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName) || string.IsNullOrWhiteSpace(attachment.ContentType))
            {
                throw new EmailSendException("Attachment FileName and ContentType are required.");
            }

            var hasBytes = attachment.Content is not null;
            var hasBlob = !string.IsNullOrWhiteSpace(attachment.BlobPath);

            if (hasBytes == hasBlob)
            {
                throw new EmailSendException("Exactly one of Content or BlobPath must be set on EmailAttachmentRef.");
            }

            if (hasBytes)
            {
                resolved.Add(new ResolvedAttachment(attachment.FileName, attachment.ContentType, attachment.Content!));
                continue;
            }

            if (blob is null)
            {
                throw new EmailSendException("IBlobStorageConnector must be registered to fetch blob attachments.");
            }

            try
            {
                await using var download = await blob.DownloadAsync(attachment.BlobPath!, ct);
                await using var ms = new MemoryStream();
                await download.Content.CopyToAsync(ms, ct);
                resolved.Add(new ResolvedAttachment(attachment.FileName, attachment.ContentType, ms.ToArray()));
            }
            catch (Exception ex)
            {
                throw new EmailSendException($"Failed to fetch blob attachment '{attachment.BlobPath}'.", ex);
            }
        }

        return resolved;
    }

    protected sealed record ResolvedAttachment(string FileName, string ContentType, byte[] Content);
}
