using Explore.Application.Contracts.Webhooks;

namespace Explore.API.BackgroundServices;

public sealed class SvixWebhookEventTypeSyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SvixWebhookEventTypeSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IWebhookProviderEventTypeSyncService>();
            var result = await syncService.SyncAsync(stoppingToken);

            if (result.Failures.Count > 0)
            {
                logger.LogWarning(
                    "Svix webhook event type sync completed with {SyncedCount} synced and {FailureCount} failures",
                    result.SyncedCount,
                    result.Failures.Count);
                return;
            }

            if (result.SyncedCount > 0)
            {
                logger.LogInformation(
                    "Svix webhook event type sync completed with {SyncedCount} event types synced",
                    result.SyncedCount);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Svix webhook event type sync stopped before completion");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Svix webhook event type sync failed. FailureType={FailureType}",
                ex.GetType().Name);
        }
    }
}
