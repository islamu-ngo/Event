using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class EventLocationAttachmentService(
    IEventLocationRepository eventLocationRepository,
    IUserContext userContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
{
    public async Task<EventLocation> ResolveAsync(
        Guid eventId,
        Guid? locationId,
        Guid? currentEventLocationId,
        CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event id is required.", nameof(eventId));
        }

        if (currentEventLocationId.HasValue)
        {
            EventLocation? current = await eventLocationRepository.GetForUpdateAsync(
                currentEventLocationId.Value,
                cancellationToken);
            if (current is not null
                && current.EventId == eventId
                && current.LocationId == locationId)
            {
                return current;
            }
        }

        EventLocation? existing = locationId.HasValue
            ? await eventLocationRepository.FindActivePhysicalAsync(eventId, locationId.Value, cancellationToken)
            : await eventLocationRepository.FindActiveToBeAnnouncedAsync(eventId, cancellationToken);
        if (existing is not null)
        {
            return await eventLocationRepository.GetForUpdateAsync(existing.Id, cancellationToken)
                ?? throw new InvalidOperationException("The active EventLocation disappeared while it was being attached.");
        }

        Guid actorUserId = userContext.GetRequiredUserId();
        DateTime createdAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        EventLocation created = locationId.HasValue
            ? EventLocation.CreatePhysical(
                tenantContext.TenantId,
                eventId,
                locationId.Value,
                actorUserId,
                createdAtUtc)
            : EventLocation.CreateToBeAnnounced(
                tenantContext.TenantId,
                eventId,
                actorUserId,
                createdAtUtc);
        return await eventLocationRepository.AddAsync(created, cancellationToken);
    }

    public async Task DetachIfUnreferencedAsync(
        Guid? eventLocationId,
        CancellationToken cancellationToken)
    {
        if (!eventLocationId.HasValue)
        {
            return;
        }

        EventLocation? eventLocation = await eventLocationRepository.GetForUpdateAsync(
            eventLocationId.Value,
            cancellationToken);
        if (eventLocation is null
            || await eventLocationRepository.HasActiveCarrierReferencesAsync(
                eventLocationId.Value,
                cancellationToken))
        {
            return;
        }

        eventLocation.DetachFinalReference(
            userContext.GetRequiredUserId(),
            timeProvider.GetUtcNow().UtcDateTime);
        await eventLocationRepository.SaveChangesAsync(cancellationToken);
    }
}
