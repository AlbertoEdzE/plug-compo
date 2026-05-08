using System.Net;
using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Internal;
using KSquare.EmailSend.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace KSquare.EmailSend.Providers;

public sealed class SendGridEmailSender : EmailSenderBase
{
    private readonly EmailSendOptions _options;
    private readonly ILogger<SendGridEmailSender> _logger;
    private readonly SendGridClient _client;
    private readonly ResiliencePipeline<EmailSendResult> _pipeline;

    public SendGridEmailSender(
        EmailSendOptions options,
        IEmailTemplateRenderer templates,
        IServiceProvider services,
        ILogger<SendGridEmailSender> logger
    )
        : base(options, templates, services)
    {
        _options = options;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(options.SendGridApiKey))
        {
            throw new InvalidOperationException("SendGridApiKey must be set for SendGrid provider.");
        }

        _client = CreateClient(options);
        _pipeline = BuildRetryPipeline(options, logger);
    }

    public override async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var normalized = NormalizeMessage(message);

        try
        {
            return await _pipeline.ExecuteAsync(async token => await SendOnceAsync(normalized, token), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed via SendGrid");
            return new EmailSendResult(false, null, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    private async Task<EmailSendResult> SendOnceAsync(EmailMessage message, CancellationToken ct)
    {
        var attachments = await ResolveAttachmentsAsync(message.Attachments, ct);

        var from = new SendGrid.Helpers.Mail.EmailAddress(message.From.Address, message.From.DisplayName);
        var msg = new SendGridMessage
        {
            From = from,
            Subject = message.Subject,
            HtmlContent = message.HtmlBody,
            PlainTextContent = message.TextBody
        };

        foreach (var to in message.To)
        {
            msg.AddTo(new SendGrid.Helpers.Mail.EmailAddress(to.Address, to.DisplayName));
        }

        foreach (var cc in message.Cc)
        {
            msg.AddCc(new SendGrid.Helpers.Mail.EmailAddress(cc.Address, cc.DisplayName));
        }

        foreach (var bcc in message.Bcc)
        {
            msg.AddBcc(new SendGrid.Helpers.Mail.EmailAddress(bcc.Address, bcc.DisplayName));
        }

        if (!string.IsNullOrWhiteSpace(message.ReplyToAddress))
        {
            msg.ReplyTo = new SendGrid.Helpers.Mail.EmailAddress(message.ReplyToAddress);
        }

        foreach (var header in message.Headers)
        {
            msg.AddHeader(header.Key, header.Value);
        }

        foreach (var attachment in attachments)
        {
            msg.AddAttachment(
                attachment.FileName,
                Convert.ToBase64String(attachment.Content),
                attachment.ContentType
            );
        }

        var response = await _client.SendEmailAsync(msg, ct);
        var statusCode = response.StatusCode;

        if (statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            throw new TransientEmailSendException($"SendGrid transient failure: {(int)statusCode}.");
        }

        if ((int)statusCode >= 400)
        {
            var body = await ReadBodyAsync(response, ct);
            return new EmailSendResult(false, TryGetMessageId(response), body, DateTimeOffset.UtcNow);
        }

        return new EmailSendResult(true, TryGetMessageId(response), null, DateTimeOffset.UtcNow);
    }

    private static ResiliencePipeline<EmailSendResult> BuildRetryPipeline(EmailSendOptions options, ILogger logger)
    {
        var retry = new RetryStrategyOptions<EmailSendResult>
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = options.RetryBaseDelay,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder<EmailSendResult>()
                .Handle<TransientEmailSendException>()
                .Handle<HttpRequestException>(),
            OnRetry = args =>
            {
                logger.LogWarning(
                    args.Outcome.Exception,
                    "Retrying email send (attempt {Attempt}/{MaxAttempts}) after {DelayMs}ms",
                    args.AttemptNumber + 1,
                    options.MaxRetryAttempts,
                    args.RetryDelay.TotalMilliseconds
                );
                return default;
            }
        };

        return new ResiliencePipelineBuilder<EmailSendResult>()
            .AddRetry(retry)
            .Build();
    }

    private static SendGridClient CreateClient(EmailSendOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SendGridBaseUrl))
        {
            return new SendGridClient(options.SendGridApiKey);
        }

        var clientOptions = new SendGridClientOptions
        {
            ApiKey = options.SendGridApiKey,
            Host = options.SendGridBaseUrl
        };

        return new SendGridClient(clientOptions);
    }

    private static string? TryGetMessageId(Response response)
    {
        foreach (var (key, value) in response.Headers)
        {
            if (key.Equals("X-Message-Id", StringComparison.OrdinalIgnoreCase))
            {
                return value.FirstOrDefault();
            }
        }

        return null;
    }

    private static async Task<string?> ReadBodyAsync(Response response, CancellationToken ct)
    {
        try
        {
            if (response.Body is null)
            {
                return null;
            }

            return await response.Body.ReadAsStringAsync(ct);
        }
        catch
        {
            return null;
        }
    }
}
