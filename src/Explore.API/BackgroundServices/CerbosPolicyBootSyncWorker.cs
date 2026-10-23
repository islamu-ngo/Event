using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

/// <summary>
/// Performs read-only authorization readiness checks once per process boot.
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
            logger.LogDebug("Cerbos publication readiness worker stopped before readiness was established");
        }
    }
}
