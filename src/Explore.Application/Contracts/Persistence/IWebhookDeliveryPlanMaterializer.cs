using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record WebhookDeliveryMaterialization(
    WebhookMessage Message,
    WebhookDeliveryPlanSnapshot DeliveryPlan,
    IReadOnlyCollection<WebhookLocalTargetSnapshot> LocalTargets,
    IReadOnlyCollection<WebhookProviderPublication> ProviderPublications);

public sealed record WebhookDeliveryMaterializationResult(
    WebhookMessage Message,
    WebhookDeliveryPlanSnapshot DeliveryPlan,
    bool Created);

public interface IWebhookDeliveryPlanMaterializer
{
    Task<WebhookDeliveryMaterializationResult> MaterializeAsync(
        WebhookDeliveryMaterialization materialization,
        CancellationToken cancellationToken);
}
