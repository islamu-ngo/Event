using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

/// <summary>
/// Background hosted service that performs zero-touch authorization reconciliation once per process boot.
/// </summary>
public sealed class CerbosPolicyBootSyncWorker(
    CerbosPolicyBootSyncRunner runner,
    IOptions<CerbosPolicyBootSyncOptions> options,
    ILogger<CerbosPolicyBootSyncWorker> logger) : BackgroundService
{
    private readonly CerbosPolicyBootSyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var initialDelay = TimeSpan.FromSeconds(Math.Max(0, _options.InitialDelaySeconds));
            if (initialDelay > TimeSpan.Zero)
            {
                await Task.Delay(initialDelay, stoppingToken).ConfigureAwait(false);
            }

            await runner.RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Cerbos policy boot sync worker stopped before publishing");
        }
    }
}
