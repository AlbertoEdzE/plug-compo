using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Contracts;

public interface IEmailIngestionConnector
{
    Task<EmailIngestionBatchResult> PollAndProcessAsync(CancellationToken ct = default);
}
