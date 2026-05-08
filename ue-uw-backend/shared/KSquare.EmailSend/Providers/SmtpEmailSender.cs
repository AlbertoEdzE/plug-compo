using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace KSquare.EmailSend.Providers;

public sealed class SmtpEmailSender : EmailSenderBase
{
    private readonly EmailSendOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        EmailSendOptions options,
        IEmailTemplateRenderer templates,
        IServiceProvider services,
        ILogger<SmtpEmailSender> logger
    )
        : base(options, templates, services)
    {
        _options = options;
        _logger = logger;
    }

    public override async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var normalized = NormalizeMessage(message);

        try
        {
            if (string.IsNullOrWhiteSpace(_options.SmtpHost))
            {
                throw new InvalidOperationException("SmtpHost must be set for Smtp provider.");
            }

            var attachments = await ResolveAttachmentsAsync(normalized.Attachments, ct);

            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(normalized.From.DisplayName, normalized.From.Address));

            foreach (var to in normalized.To)
            {
                mime.To.Add(new MailboxAddress(to.DisplayName, to.Address));
            }

            foreach (var cc in normalized.Cc)
            {
                mime.Cc.Add(new MailboxAddress(cc.DisplayName, cc.Address));
            }

            foreach (var bcc in normalized.Bcc)
            {
                mime.Bcc.Add(new MailboxAddress(bcc.DisplayName, bcc.Address));
            }

            if (!string.IsNullOrWhiteSpace(normalized.ReplyToAddress))
            {
                mime.ReplyTo.Add(MailboxAddress.Parse(normalized.ReplyToAddress));
            }

            foreach (var header in normalized.Headers)
            {
                mime.Headers[header.Key] = header.Value;
            }

            mime.Subject = normalized.Subject;

            var builder = new BodyBuilder
            {
                HtmlBody = normalized.HtmlBody,
                TextBody = normalized.TextBody
            };

            foreach (var attachment in attachments)
            {
                builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
            }

            mime.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            var socket = _options.SmtpUseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socket, ct);

            if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
            {
                if (_options.SmtpPassword is null)
                {
                    throw new InvalidOperationException("SmtpPassword must be set when SmtpUsername is provided.");
                }

                await client.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword, ct);
            }

            var messageId = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            return new EmailSendResult(true, messageId, null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed via SMTP");
            return new EmailSendResult(false, null, ex.Message, DateTimeOffset.UtcNow);
        }
    }
}
