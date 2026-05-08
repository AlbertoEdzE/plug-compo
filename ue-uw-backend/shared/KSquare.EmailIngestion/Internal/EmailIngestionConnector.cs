using KSquare.BlobStorage.Exceptions;
using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Contracts;
using KSquare.EmailIngestion.Models;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Models;
using Microsoft.Extensions.Logging;

namespace KSquare.EmailIngestion.Internal;

internal sealed class EmailIngestionConnector(
    EmailIngestionOptions options,
    IEmailSource source,
    IEmailMover mover,
    IEmailParser parser,
    IEmailDuplicateDetector duplicates,
    BlobAttachmentStore attachmentStore,
    IEventPublisher events,
    ILogger<EmailIngestionConnector> logger
) : IEmailIngestionConnector
{
    private const long MaxAttachmentBytes = 50L * 1024 * 1024;

    public async Task<EmailIngestionBatchResult> PollAndProcessAsync(CancellationToken ct = default)
    {
        var processedAt = DateTimeOffset.UtcNow;
        var fetched = 0;
        var processed = 0;
        var dupes = 0;
        var errors = 0;

        IReadOnlyList<FetchedEmail> emails;
        try
        {
            emails = await source.FetchUnreadAsync(ct);
            fetched = emails.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch emails from source.");
            return new EmailIngestionBatchResult(0, 0, 0, 1, processedAt);
        }

        foreach (var item in emails)
        {
            ct.ThrowIfCancellationRequested();

            var correlationId = Guid.NewGuid().ToString();
            EmailMessage? email = null;

            try
            {
                email = parser.Parse(item.RawMimeBytes);

                var dateBucket = email.ReceivedAt.UtcDateTime.ToString("yyyy-MM-dd");
                var firstAttachmentMd5 = email.Attachments.Count > 0 ? EmailFingerprintHasher.Md5Of(email.Attachments[0].Content) : null;
                var fingerprint = new EmailFingerprint(email.FromAddress, email.Subject, dateBucket, firstAttachmentMd5);

                if (await duplicates.IsDuplicateAsync(fingerprint, ct))
                {
                    dupes++;
                    logger.LogInformation("Duplicate email detected for messageId={MessageId}", email.MessageId);
                    await mover.MarkReadAndMoveToProcessedAsync(item.SourceMessageId, ct);
                    continue;
                }

                foreach (var attachment in email.Attachments)
                {
                    if (attachment.SizeBytes > MaxAttachmentBytes)
                    {
                        var rawPath = await attachmentStore.StoreRawEmailAsync(item.RawMimeBytes, email.ReceivedAt, correlationId, ct);
                        await events.PublishAsync(
                            options.EventTopic,
                            "email.attachment_oversized",
                            new EmailAttachmentOversizedEvent
                            {
                                CorrelationId = correlationId,
                                MessageId = email.MessageId,
                                FileName = attachment.FileName,
                                SizeBytes = attachment.SizeBytes,
                                ReceivedAt = email.ReceivedAt
                            },
                            new EventPublishOptions { CorrelationId = correlationId, MessageId = email.MessageId },
                            ct
                        );

                        await mover.MarkReadAndMoveToProcessedAsync(item.SourceMessageId, ct);
                        await duplicates.MarkProcessedAsync(fingerprint, ct);
                        processed++;
                        goto NextEmail;
                    }
                }

                var rawBlobPath = await attachmentStore.StoreRawEmailAsync(item.RawMimeBytes, email.ReceivedAt, correlationId, ct);

                var attachmentBlobPaths = new List<string>(email.Attachments.Count);
                foreach (var attachment in email.Attachments)
                {
                    var path = await attachmentStore.StoreAsync(attachment, correlationId, ct);
                    attachmentBlobPaths.Add(path);
                }

                var hint = IntentHintDetector.DetectIntentHint(email);

                var receivedEvent = new EmailReceivedEvent
                {
                    CorrelationId = correlationId,
                    MessageId = email.MessageId,
                    FromAddress = email.FromAddress,
                    Subject = email.Subject,
                    RawEmailBlobPath = rawBlobPath,
                    AttachmentBlobPaths = attachmentBlobPaths,
                    AttachmentCount = email.Attachments.Count,
                    ReceivedAt = email.ReceivedAt,
                    DetectedIntentHint = hint
                };

                await events.PublishAsync(
                    options.EventTopic,
                    "email.received",
                    receivedEvent,
                    new EventPublishOptions { CorrelationId = correlationId, MessageId = email.MessageId },
                    ct
                );

                await mover.MarkReadAndMoveToProcessedAsync(item.SourceMessageId, ct);
                await duplicates.MarkProcessedAsync(fingerprint, ct);
                processed++;
            }
            catch (BlobAuthException ex)
            {
                errors++;
                logger.LogError(ex, "Blob auth failure while processing email. Email not marked as read.");
            }
            catch (BlobNotFoundException ex)
            {
                errors++;
                logger.LogError(ex, "Blob not found while processing email. Email not marked as read.");
            }
            catch (Exception ex) when (email is null)
            {
                errors++;
                logger.LogError(ex, "Email parse failed. Attempting to store raw bytes and publish parse_failed.");

                try
                {
                    var correlationId2 = Guid.NewGuid().ToString();
                    var rawBlobPath = await attachmentStore.StoreRawEmailAsync(item.RawMimeBytes, item.ReceivedAt, correlationId2, ct);

                    await events.PublishAsync(
                        options.EventTopic,
                        "email.parse_failed",
                        new EmailParseFailedEvent
                        {
                            CorrelationId = correlationId2,
                            SourceMessageId = item.SourceMessageId,
                            RawEmailBlobPath = rawBlobPath,
                            Error = ex.Message,
                            ReceivedAt = item.ReceivedAt
                        },
                        new EventPublishOptions { CorrelationId = correlationId2 },
                        ct
                    );

                    await mover.MarkReadAndMoveToProcessedAsync(item.SourceMessageId, ct);
                }
                catch (Exception inner)
                {
                    logger.LogError(inner, "Failed to store or publish parse_failed. Email not marked as read.");
                }
            }
            catch (Exception ex)
            {
                errors++;
                logger.LogError(ex, "Error while processing email. Email not marked as read.");
            }

            NextEmail:
            continue;
        }

        return new EmailIngestionBatchResult(fetched, processed, dupes, errors, processedAt);
    }
}
