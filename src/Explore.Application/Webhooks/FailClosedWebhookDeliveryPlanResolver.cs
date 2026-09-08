using Explore.Application.Contracts.Webhooks;

namespace Explore.Application.Webhooks;

public sealed class FailClosedWebhookDeliveryPlanResolver : IWebhookDeliveryPlanResolver
{
    public Task<WebhookDeliveryPlanResolution> ResolveAsync(
        WebhookEventBuildContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WebhookDeliveryPlanResolution.Unavailable(
            "webhook_delivery_plan_unavailable",
            "Verified webhook delivery-plan resolution is not configured."));
    }
}
