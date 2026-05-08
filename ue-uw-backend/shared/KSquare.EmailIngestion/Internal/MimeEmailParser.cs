using System.Net;
using System.Text.RegularExpressions;
using MimeKit;
using KSquare.EmailIngestion.Contracts;
using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Internal;

internal sealed class MimeEmailParser : IEmailParser
{
    private static readonly Regex TagsRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);

    public EmailMessage Parse(byte[] rawEmail)
    {
        using var ms = new MemoryStream(rawEmail);
        var message = MimeMessage.Load(ms);

        var fromMailbox = message.From.Mailboxes.FirstOrDefault();
        var toMailbox = message.To.Mailboxes.FirstOrDefault();

        var headers = message.Headers.ToDictionary(h => h.Field, h => h.Value, StringComparer.OrdinalIgnoreCase);

        var bodyText = message.TextBody;
        var bodyHtml = message.HtmlBody;

        if (string.IsNullOrWhiteSpace(bodyText) && !string.IsNullOrWhiteSpace(bodyHtml))
        {
            bodyText = HtmlToText(bodyHtml);
        }

        bodyText ??= string.Empty;

        var attachments = new List<EmailAttachment>();
        foreach (var attachment in message.Attachments)
        {
            if (attachment is not MimePart part)
            {
                continue;
            }

            if (part.ContentDisposition is null)
            {
                continue;
            }

            if (!part.ContentDisposition.Disposition.Equals(ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var attachmentStream = new MemoryStream();
            part.Content?.DecodeTo(attachmentStream);

            attachments.Add(new EmailAttachment
            {
                FileName = part.FileName ?? "attachment",
                ContentType = part.ContentType.MimeType,
                Content = attachmentStream.ToArray()
            });
        }

        var messageId = message.MessageId;
        if (string.IsNullOrWhiteSpace(messageId))
        {
            messageId = headers.TryGetValue("Message-Id", out var msgIdHeader) ? msgIdHeader : Guid.NewGuid().ToString("N");
        }

        return new EmailMessage
        {
            MessageId = messageId,
            Subject = message.Subject ?? string.Empty,
            FromAddress = fromMailbox?.Address ?? string.Empty,
            FromName = fromMailbox?.Name ?? string.Empty,
            ToAddress = toMailbox?.Address,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            Attachments = attachments,
            ReceivedAt = message.Date != DateTimeOffset.MinValue ? message.Date : DateTimeOffset.UtcNow,
            Headers = headers
        };
    }

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = TagsRegex.Replace(html, " ");
        var normalized = WhitespaceRegex.Replace(withoutTags, " ").Trim();
        return WebUtility.HtmlDecode(normalized);
    }
}
