using Explore.Application.Contracts.Webhooks;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Svix;

namespace Explore.Infrastructure.Webhooks;

public sealed class SvixEventTypeSyncService(
    ISvixWebhookClient svixClient,
    IWebhookEventTypeRegistry eventTypeRegistry,
    IWebhookEventSchemaProvider schemaProvider,
    IOptionsMonitor<WebhookOptions> options) : IWebhookProviderEventTypeSyncService
{
    public async Task<WebhookProviderEventTypeSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentOptions = options.CurrentValue;
        if (currentOptions.IsDisabled
            || !currentOptions.Svix.SyncEventTypesOnStartup
            || !(currentOptions.IsProvider(WebhookOptions.ProviderSvix)
                 || currentOptions.IsProvider(WebhookOptions.ProviderComposite)))
        {
            return WebhookProviderEventTypeSyncResult.Success(0);
        }

        var synced = 0;
        List<WebhookProviderEventTypeSyncFailure> failures = [];

        foreach (var descriptor in eventTypeRegistry.GetAll().Where(descriptor => descriptor.IsPublic && descriptor.IsEnabled))
        {
            try
            {
                await svixClient.UpsertEventTypeAsync(
                    new SvixEventTypeSyncRequest(
                        descriptor.Name,
                        descriptor.Description,
                        descriptor.GroupName,
                        schemaProvider.CreateSchemaJson(descriptor),
                        $"svix-event-type:{descriptor.Name}:v{descriptor.SchemaVersion}"),
                    cancellationToken);
                synced++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SvixWebhookConfigurationException ex)
            {
                failures.Add(new WebhookProviderEventTypeSyncFailure(
                    descriptor.Name,
                    ex.FailureCategory,
                    IsRetryable: false,
                    ex.FailureCategory));
                break;
            }
            catch (ApiException ex)
            {
                var failure = SvixWebhookFailureClassifier.Classify(ex);
                failures.Add(new WebhookProviderEventTypeSyncFailure(
                    descriptor.Name,
                    failure.Category,
                    failure.IsRetryable,
                    failure.SafeDetail));

                if (!failure.IsRetryable)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                failures.Add(new WebhookProviderEventTypeSyncFailure(
                    descriptor.Name,
                    "svix_event_type_sync_failed",
                    IsRetryable: true,
                    ex.GetType().Name));
            }
        }

        return WebhookProviderEventTypeSyncResult.Completed(synced, failures);
    }
}
