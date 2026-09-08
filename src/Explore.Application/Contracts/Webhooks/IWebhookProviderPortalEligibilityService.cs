namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookProviderPortalEligibilityService
{
    Task<IReadOnlySet<Guid>> GetEligibleConsumerIdsAsync(
        Guid? tenantId,
        IReadOnlyCollection<Guid> consumerIds,
        CancellationToken cancellationToken);
}
