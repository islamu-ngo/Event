// ABOUTME: Persistence contract for registration order creation and capacity-hold reservation primitives.
// ABOUTME: Exposes entity-first transaction-bound operations for application orchestration.

using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationInventoryRepository
{
    Task<RegistrationOrder?> GetOrderByIdAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetOrderWithPiiAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetOrderWithLinesAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetOrderForUpdateWithLinesAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetOrderForUpdateWithPiiAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationInventoryHold>> GetHoldsByOrderAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationInventoryHold>> GetActiveHoldsForUpdateAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationOrder>> GetOrdersByEventAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<bool> HasPaidEvidenceAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetRegisteredUserFanoutBatchAsync(
        Guid tenantId,
        Guid eventId,
        Guid? afterUserId,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationFanoutAudienceMember>> GetNotificationFanoutAudienceBatchAsync(
        Guid tenantId,
        Guid eventId,
        Guid? sessionId,
        DateTime audienceCutoffAt,
        int deliveryPolicyId,
        NotificationFanoutAudienceCursor? cursor,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventCapacityPool>> GetPoolsForUpdateAsync(
        IReadOnlyCollection<Guid> capacityPoolIds,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<int> GetAllocatedQuantityAsync(Guid capacityPoolId, Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, RegistrationTicketLimitUsage>> GetTicketLimitUsageAsync(
        Guid eventId,
        Guid tenantId,
        Guid? accountUserId,
        string? verifiedContactNormalizedEmail,
        Guid? bookingPartyActorId,
        IReadOnlyCollection<Guid> ticketTypeIds,
        CancellationToken cancellationToken);

    Task AddOrderWithHoldsAsync(
        RegistrationOrder order,
        IReadOnlyCollection<RegistrationInventoryHold> holds,
        CancellationToken cancellationToken);

    Task<RegistrationInventoryReservationResult> ReserveNonTimedHoldsAsync(
        Guid eventId,
        Guid tenantId,
        IReadOnlyCollection<RegistrationInventoryReservation> reservations,
        bool approvalGranted,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<RegistrationInventoryReservationResult> ReserveRecoveredHoldsAsync(
        Guid eventId,
        Guid tenantId,
        IReadOnlyCollection<RegistrationInventoryReservation> reservations,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationInventoryHold>> GetExpiredActiveHoldsAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationHoldExpiryRecoveryTarget>> GetHoldExpiryRecoveryTargetsAsync(
        int batchSize,
        CancellationToken cancellationToken);

    Task<bool> TryExpireDueHoldAsync(Guid holdId, DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> TryConsumeActiveHoldAsync(Guid holdId, DateTime utcNow, CancellationToken cancellationToken);

    Task<int> TryConsumeActiveHoldsForOrderAsync(
        Guid orderId,
        Guid tenantId,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<bool> TryReleaseActiveHoldAsync(
        Guid holdId,
        RegistrationInventoryHoldStatusEnum outcome,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<int> TryReleaseActiveHoldsForOrderAsync(
        Guid orderId,
        Guid tenantId,
        RegistrationInventoryHoldStatusEnum outcome,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task AddEventRegistrationsAsync(
        IReadOnlyCollection<EventRegistration> registrations,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record RegistrationTicketLimitUsage(
    Guid TicketTypeId,
    int AccountQuantity,
    int VerifiedContactQuantity,
    int BookingPartyQuantity);

public sealed record RegistrationInventoryReservation(
    Guid HoldId,
    Guid RegistrationOrderId,
    Guid CapacityPoolId,
    Guid TicketTypeId,
    int Quantity);

public sealed record RegistrationInventoryReservationResult(
    bool Reserved,
    bool RequiresApproval,
    bool ShouldWaitlist);

public sealed record RegistrationHoldExpiryRecoveryTarget(Guid TenantId, Guid RegistrationOrderId);
