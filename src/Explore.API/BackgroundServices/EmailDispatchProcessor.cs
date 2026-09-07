using Explore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

public sealed class EmailDispatchProcessor(
    EmailDispatchHostedDrainRunner drainRunner,
    IOptions<EmailDispatchProcessorSettings> settings,
    ILogger<EmailDispatchProcessor> logger) : BackgroundService
{
    private readonly EmailDispatchProcessorSettings _settings = settings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Email dispatch processor is disabled");
            return;
        }

        logger.LogInformation(
            "Email dispatch processor starting with {Interval}s interval and batch size {BatchSize}",
            _settings.PollingIntervalSeconds,
            _settings.BatchSize);

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await drainRunner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in email dispatch processor loop");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Email dispatch processor stopped");
    }

}
