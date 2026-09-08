using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record WebhookProviderPublicationClaimRequest(
    int BatchSize,
    string LeaseOwner,
    DateTime ClaimedAt,
    TimeSpan LeaseDuration,
    int MaxAutomaticAttempts);

public sealed record WebhookProviderPublicationClaim(
    WebhookProviderPublication Publication,
    Guid LeaseToken,
    long PublicationFence,
    DateTime ClaimedAt,
    DateTime LeaseExpiresAt,
    DateTime DueAt);

public sealed class WebhookProviderPublicationConcurrencyException : InvalidOperationException
{
    public WebhookProviderPublicationConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface IWebhookProviderPublicationRepository
{
    Task<WebhookProviderPublication> CreateAsync(
        WebhookProviderPublication publication,
        CancellationToken cancellationToken);

    Task<WebhookProviderPublication?> GetByIdentityAsync(
        Guid tenantId,
        Guid webhookMessageId,
        WebhookProviderKind providerKind,
        Guid providerBindingId,
        CancellationToken cancellationToken);

    Task<WebhookProviderPublication?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid publicationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookProviderPublication>> ListByTenantAsync(
        Guid tenantId,
        Guid? webhookMessageId,
        Guid? webhookConsumerId,
        int? statusId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookProviderPublicationClaim>> ClaimDueAsync(
        WebhookProviderPublicationClaimRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookProviderPublicationClaim>> ClaimUnknownAsync(
        WebhookProviderPublicationClaimRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookProviderPublication>> GetUnknownRequiringManualAsync(
        DateTime observedAt,
        int batchSize,
        int maxAutomaticReconciliationAttempts,
        CancellationToken cancellationToken);

    Task<int> CountUncertainByConsumerAsync(
        Guid? tenantId,
        Guid webhookConsumerId,
        CancellationToken cancellationToken);

    Task<WebhookProviderPublication?> GetActiveClaimAsync(
        Guid tenantId,
        Guid publicationId,
        Guid leaseToken,
        long publicationFence,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<WebhookProviderPublication> UpdateAsync(
        WebhookProviderPublication publication,
        CancellationToken cancellationToken);
}
