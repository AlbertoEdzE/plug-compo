using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Contracts;

public interface IEmailAttachmentStore
{
    Task<string> StoreAsync(EmailAttachment attachment, string correlationId, CancellationToken ct = default);
}
