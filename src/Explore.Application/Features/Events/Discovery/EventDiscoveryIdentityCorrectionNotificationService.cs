using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Application.Notifications;
using Explore.Domain;

namespace Explore.Application.Features.Events.Discovery;

public sealed class EventDiscoveryIdentityCorrectionNotificationService(
    IEventRepository eventRepository,
    IEventRoleAssignmentRepository eventRoleAssignmentRepository,
    ITenantUserRepository tenantUserRepository,
    IRecipientNotificationMaterializer notificationMaterializer,
    EventDiscoveryIdentityCorrectionNotificationFactory notificationFactory,
    TimeProvider timeProvider) : IEventDiscoveryIdentityCorrectionNotificationService
{
    public async Task DeliverAsync(
        Guid outboxMessageId,
        EventDiscoveryIdentityCorrectionRequested request,
        CancellationToken cancellationToken = default)
    {
        Validate(outboxMessageId, request);

        Guid[] requestedEventIds = [request.EventId, request.PrimaryEventId];
        IReadOnlyList<Event> loadedEvents = await eventRepository.GetAuthorizationTargetsByIdsAsync(
            requestedEventIds,
            cancellationToken);
        if (loadedEvents.Any(@event => @event.TenantId != request.TenantId))
        {
            throw new InvalidOperationException("Discovery correction event authority crossed a tenant boundary.");
        }

        DateTime authorityAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var recipientEvents = new Dictionary<Guid, Guid>();
        foreach (Event sourceEvent in loadedEvents
                     .Where(@event => !@event.IsDeleted)
                     .OrderBy(@event => @event.Id))
        {
            IReadOnlyList<EventRoleAssignment> owners =
                await eventRoleAssignmentRepository.GetEffectiveOwnersForEventAsync(
                    request.TenantId,
                    sourceEvent.Id,
                    authorityAtUtc,
                    cancellationToken);
            foreach (EventRoleAssignment owner in owners
                         .Where(owner => !owner.User.IsDeleted)
                         .GroupBy(owner => owner.UserId)
                         .Select(group => group.First())
                         .OrderBy(owner => owner.UserId))
            {
                if (!await tenantUserRepository.IsActiveTenantUserAsync(
                        request.TenantId,
                        owner.UserId,
                        cancellationToken))
                {
                    continue;
                }

                recipientEvents.TryAdd(owner.UserId, sourceEvent.Id);
            }
        }

        foreach ((Guid recipientUserId, Guid recipientEventId) in recipientEvents.OrderBy(pair => pair.Key))
        {
            RecipientNotificationMaterialization notification = notificationFactory.Create(
                outboxMessageId,
                request,
                recipientUserId,
                recipientEventId,
                authorityAtUtc);
            await notificationMaterializer.MaterializeAsync(notification, cancellationToken);
        }
    }

    private static void Validate(
        Guid outboxMessageId,
        EventDiscoveryIdentityCorrectionRequested request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (outboxMessageId == Guid.Empty
            || request.TenantId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.PrimaryEventId == Guid.Empty
            || request.EventId == request.PrimaryEventId
            || request.Revision < 0
            || request.Revision == 0 && request.Decision != "different-offering"
            || request.Decision is not ("same-offering" or "different-offering" or "reverse")
            || string.IsNullOrEmpty(request.ReasonCode)
            || request.ReasonCode.Length > 80
            || request.ReasonCode.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            throw new InvalidOperationException("Discovery identity correction payload is invalid.");
        }
    }
}
