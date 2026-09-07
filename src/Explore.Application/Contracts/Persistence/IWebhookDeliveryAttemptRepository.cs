using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebhookDeliveryAttemptRepository
{
    Task<WebhookDeliveryAttempt> CreateAsync(
        WebhookDeliveryAttempt attempt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDeliveryAttempt>> CreateManyAsync(
        IReadOnlyCollection<WebhookDeliveryAttempt> attempts,
        CancellationToken cancellationToken);

    Task<WebhookDeliveryAttempt?> GetByIdForOwnerOperationAsync(
        Guid attemptId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDeliveryAttempt>> ListByOwnerAsync(
        WebhookOwnershipScope ownership,
        Guid? messageId,
        Guid? endpointId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDeliveryAttempt>> GetByMessageAsync(
        Guid tenantId,
        Guid messageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDeliveryAttempt>> ListByTenantAsync(
        Guid tenantId,
        Guid? messageId,
        Guid? endpointId,
        int limit,
        CancellationToken cancellationToken);

    Task<WebhookDeliveryAttempt?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid attemptId,
        CancellationToken cancellationToken);
}
