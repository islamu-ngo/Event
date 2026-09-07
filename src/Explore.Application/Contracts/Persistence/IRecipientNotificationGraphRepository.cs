using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRecipientNotificationGraphRepository
{
    Task<NotificationIntent> CreateGraphAsync(
        NotificationIntent intent,
        CancellationToken cancellationToken = default);

    Task<NotificationIntent?> GetGraphByTenantAndDeduplicationKeyAsync(
        Guid tenantId,
        string deduplicationKey,
        CancellationToken cancellationToken = default);

    Task<NotificationIntent?> GetGraphByTenantOccurrenceAndRecipientAsync(
        Guid tenantId,
        Guid occurrenceId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default);

    Task RepairMissingRecipientDeliveryRowsAsync(
        NotificationIntent winningIntent,
        IReadOnlyList<NotificationDelivery> expectedDeliveries,
        Notification? expectedNotification,
        EmailDispatchOutbox? expectedEmail,
        CancellationToken cancellationToken = default);
}
