using System.Net;
using System.Text;
using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Models;
using KSquare.Notifications.Contracts;
using KSquare.Notifications.Models;
using KSquare.PiiRedaction.Contracts;
using Microsoft.Extensions.Logging;

namespace KSquare.Notifications.Channels;

public sealed class EmailNotificationChannel(
    IEmailSender emailSender,
    EmailSendOptions emailSendOptions,
    ILogger<EmailNotificationChannel> logger,
    IPiiRedactor piiRedactor
) : INotificationChannel
{
    public string ChannelName => "email";

    public async Task SendAsync(NotificationRequest request, NotificationRecipient recipient, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recipient.Email))
        {
            return;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Priority == NotificationPriority.Critical)
        {
            headers["X-Priority"] = "1";
        }

        var htmlBody = request.HtmlBody;
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            htmlBody = WrapAsHtml(request.Body, request.ActionUrl);
        }

        try
        {
            var result = await emailSender.SendAsync(new EmailMessage
            {
                From = new EmailAddress(emailSendOptions.DefaultFromAddress, emailSendOptions.DefaultFromName),
                To = [new EmailAddress(recipient.Email, recipient.DisplayName)],
                Subject = request.Title,
                HtmlBody = htmlBody,
                TextBody = request.Body,
                Headers = headers,
                CorrelationId = request.CorrelationId
            }, ct);

            if (!result.Success)
            {
                logger.LogError(
                    "Email notification send failed for userId={UserId}, eventType={EventType}, error={Error}",
                    recipient.UserId,
                    request.EventType,
                    piiRedactor.RedactValue(result.Error ?? string.Empty)
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Email notification send threw for userId={UserId}, eventType={EventType}",
                recipient.UserId,
                request.EventType
            );
        }
    }

    private static string WrapAsHtml(string body, string? actionUrl)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body>");
        sb.Append("<p>");
        sb.Append(WebUtility.HtmlEncode(body).Replace("\n", "<br/>", StringComparison.Ordinal));
        sb.Append("</p>");

        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            sb.Append("<p><a href=\"");
            sb.Append(WebUtility.HtmlEncode(actionUrl));
            sb.Append("\">Open</a></p>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }
}
