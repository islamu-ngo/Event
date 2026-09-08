using Explore.Application.Contracts.Webhooks;

namespace Explore.API.BackgroundServices;

public sealed class WebhookEventTypeCatalogSyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookEventTypeCatalogSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IWebhookEventTypeCatalogSyncService>();
            var result = await syncService.SyncAsync(stoppingToken);

            logger.LogInformation(
                "Webhook event type catalog sync completed with {CreatedCount} created, {UpdatedCount} updated, and {UnchangedCount} unchanged rows",
                result.CreatedCount,
                result.UpdatedCount,
                result.UnchangedCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Webhook event type catalog sync stopped before completion");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Webhook event type catalog sync failed. FailureType={FailureType}",
                exception.GetType().Name);
        }
    }
}
