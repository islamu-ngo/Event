using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.WebPush;

public sealed class WebPushDispatchProcessor(
    WebPushDispatchDrainService drainService,
    IOptions<WebPushSettings> options,
    ILogger<WebPushDispatchProcessor> logger) : BackgroundService
{
    private readonly WebPushSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Web Push dispatch processor is disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds));
        do
        {
            await drainService.RecoverStaleProcessingAsync(stoppingToken);
            await drainService.ProcessBatchAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
