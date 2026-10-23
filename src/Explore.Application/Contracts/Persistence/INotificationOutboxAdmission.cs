using Explore.Application.Services;
using Explore.Application.Features.Events.Discovery.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Admits only persisted notification source identities and claim fences.
/// Payloads, moderation content and occurrence snapshots never leave Global admission.
/// </summary>
public interface INotificationOutboxAdmission
{
    Task<IReadOnlyList<NotificationOutboxClaim>> ClaimDueAsync(
        int batchSize, DateTime claimedAt, CancellationToken cancellationToken);

    Task<NotificationOutboxClaim?> ReadClaimAsync(
        Guid messageId, DateTime processingLeaseExpiresAt, CancellationToken cancellationToken);
}

public sealed record NotificationOutboxClaim(
    Guid MessageId, Guid TenantId, Guid SourceId, Guid AggregateId,
    string AggregateType, string EventType, string PayloadDigest, DateTime ProcessingLeaseExpiresAt);

public static class NotificationOutboxRoutes
{
    public static bool Contains(string eventType) => eventType is
        EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType or
        EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType or
        EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType or
        NotificationFanoutOccurrenceOutboxMessageFactory.EventType or
        EventDiscoveryIdentityCorrectionRequested.EventType;
}
