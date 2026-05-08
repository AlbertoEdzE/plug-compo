using KSquare.EmailIngestion.Configuration;
using KSquare.EmailIngestion.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KSquare.EmailIngestion.HostedService;

public sealed class EmailIngestionHostedService(
    EmailIngestionOptions options,
    IEmailIngestionConnector connector,
    ILogger<EmailIngestionHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await connector.PollAndProcessAsync(stoppingToken);
                logger.LogInformation(
                    "Email ingestion batch: fetched={Fetched} processed={Processed} duplicates={Duplicates} errors={Errors}",
                    result.TotalFetched,
                    result.NewlyProcessed,
                    result.DuplicatesSkipped,
                    result.Errors
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in email ingestion hosted service.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
