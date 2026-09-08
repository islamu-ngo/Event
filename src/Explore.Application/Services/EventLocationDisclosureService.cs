using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class EventLocationDisclosureService(
    IEventLocationRepository eventLocationRepository,
    ILocationRoomRepository roomRepository,
    IEventRegistrationRepository registrationRepository,
    IEventLocationRegistrationAccessService registrationAccessService,
    ILocationPrivacyGovernanceService governanceService,
    IEventLocationManagementAuthorizationService managementAuthorizationService,
    ICurrentUserService currentUserService,
    EventLocationDisclosureEvaluator evaluator,
    EventLocationPrivacyMetrics metrics,
    TimeProvider timeProvider) : IEventLocationDisclosureService
{
    public async Task<IReadOnlyDictionary<Guid, EventLocationDisclosureResult>> ResolveManyAsync(
        IReadOnlyCollection<EventLocationDisclosureRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count > IEventLocationDisclosureService.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requests),
                $"EventLocation disclosure batches cannot exceed {IEventLocationDisclosureService.MaximumBatchSize} requests.");
        }

        if (requests.Count == 0)
        {
            return ImmutableDictionary<Guid, EventLocationDisclosureResult>.Empty;
        }

        EventLocationDisclosureRequest[] normalized = Normalize(requests);
        EventLocationDisclosureRequest first = normalized[0];
        DateTimeOffset serverNowUtc = timeProvider.GetUtcNow().ToUniversalTime();

        Guid[] eventLocationIds = normalized.Select(request => request.EventLocationId).ToArray();
        IReadOnlyList<EventLocation> eventLocations = await eventLocationRepository.GetByIdsAsync(
            eventLocationIds,
            cancellationToken);
        IReadOnlyDictionary<Guid, EventLocationDisclosureRequest> requestByEventLocationId =
            normalized.ToDictionary(request => request.EventLocationId);
        IReadOnlyDictionary<Guid, EventLocation> eventLocationById = eventLocations
            .Where(item => item.TenantId == first.TenantId
                && requestByEventLocationId.TryGetValue(item.Id, out EventLocationDisclosureRequest? request)
                && item.EventId == request.EventId)
            .ToDictionary(item => item.Id);

        Guid[] roomIds = normalized
            .Where(request => request.RoomId.HasValue)
            .Select(request => request.RoomId!.Value)
            .Distinct()
            .ToArray();
        IReadOnlyList<LocationRoom> rooms = roomIds.Length == 0
            ? []
            : await roomRepository.GetByIdsAsync(roomIds, cancellationToken);
        IReadOnlyDictionary<Guid, LocationRoom> roomById = rooms.ToDictionary(room => room.Id);

        EffectiveLocationPrivacyGovernance governance = await governanceService.ResolveAsync(
            first.TenantId,
            cancellationToken);
        IReadOnlyDictionary<Guid, EventLocationRegistrationAccess> registrationAccess =
            ImmutableDictionary<Guid, EventLocationRegistrationAccess>.Empty;
        IReadOnlyDictionary<Guid, bool> managementDecisions = ImmutableDictionary<Guid, bool>.Empty;

        if (first.Purpose == EventLocationDisclosurePurpose.Attendee)
        {
            IReadOnlyList<EventRegistration> registrations =
                await registrationRepository.GetLocationAccessCoverageAsync(
                    first.TenantId,
                    first.EventId,
                    first.RequesterUserId!.Value,
                    cancellationToken);
            registrationAccess = registrationAccessService.ResolveMany(
                first.TenantId,
                first.EventId,
                first.RequesterUserId.Value,
                serverNowUtc,
                eventLocationIds,
                registrations);
        }
        else if (first.Purpose == EventLocationDisclosurePurpose.Management)
        {
            managementDecisions = await managementAuthorizationService.AuthorizeManyAsync(
                eventLocationById.Values.ToArray(),
                EventLocationExactReadPurposeEnum.EventManagement,
                correlationId: null,
                traceId: null,
                cancellationToken);
        }

        var governanceFact = new EventLocationDisclosureGovernanceFact(
            governance.IsResolved,
            governance.AllowHomeLocations,
            governance.AllowPublicExactAddress,
            governance.AllowPublicCoordinates,
            governance.MinimumHomeAudience,
            governance.DefaultRevealOffset);
        var results = ImmutableDictionary.CreateBuilder<Guid, EventLocationDisclosureResult>();

        foreach (EventLocationDisclosureRequest request in normalized)
        {
            eventLocationById.TryGetValue(request.EventLocationId, out EventLocation? eventLocation);
            LocationRoom? room = request.RoomId is { } roomId
                ? roomById.GetValueOrDefault(roomId)
                : null;
            EventLocationDisclosureAuthorityFact? authority = CreateAuthority(
                request,
                registrationAccess,
                managementDecisions);

            EventLocationDisclosureResult result = evaluator.Evaluate(new(
                request,
                eventLocation,
                eventLocation?.Location,
                room,
                governanceFact,
                authority,
                serverNowUtc,
                Derivatives: null));

            metrics.RecordDisclosure(request.Purpose, result.State);
            results.Add(request.EventLocationId, result);
        }

        return results.ToImmutable();
    }

    private EventLocationDisclosureRequest[] Normalize(
        IReadOnlyCollection<EventLocationDisclosureRequest> requests)
    {
        EventLocationDisclosureRequest[] candidates = requests.ToArray();
        foreach (EventLocationDisclosureRequest request in candidates)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.TenantId == Guid.Empty
                || request.EventId == Guid.Empty
                || request.EventLocationId == Guid.Empty
                || request.RoomId == Guid.Empty
                || !Enum.IsDefined(request.Purpose))
            {
                throw new ArgumentException("EventLocation disclosure requests require valid scoped identities and purpose.", nameof(requests));
            }
        }

        EventLocationDisclosureRequest first = candidates[0];
        if (candidates.Any(request => request.TenantId != first.TenantId
            || request.Purpose != first.Purpose
            || first.Purpose != EventLocationDisclosurePurpose.Public
                && request.EventId != first.EventId))
        {
            throw new ArgumentException(
                "EventLocation disclosure batches require one tenant and purpose; private batches also require one event.",
                nameof(requests));
        }

        Guid? requesterUserId = null;
        if (first.Purpose != EventLocationDisclosurePurpose.Public)
        {
            requesterUserId = currentUserService.IsAuthenticated
                ? currentUserService.UserId
                : null;
            if (requesterUserId is null
                || requesterUserId == Guid.Empty
                || candidates.Any(request => request.RequesterUserId is { } supplied && supplied != requesterUserId))
            {
                string action = first.Purpose == EventLocationDisclosurePurpose.Management
                    ? AuthorizationActions.Events.ViewManagement
                    : AuthorizationActions.Events.View;
                throw new AuthorizationException(ResourceKinds.Event, action);
            }
        }

        EventLocationDisclosureRequest[] scoped = candidates
            .Select(request => request with { RequesterUserId = requesterUserId })
            .ToArray();
        return scoped
            .GroupBy(request => request.EventLocationId)
            .Select(group =>
            {
                EventLocationDisclosureRequest request = group.First();
                Guid?[] roomIds = group
                    .Select(item => item.RoomId)
                    .Distinct()
                    .Take(2)
                    .ToArray();
                return request with { RoomId = roomIds.Length == 1 ? roomIds[0] : null };
            })
            .OrderBy(request => request.EventLocationId)
            .ToArray();
    }

    private static EventLocationDisclosureAuthorityFact? CreateAuthority(
        EventLocationDisclosureRequest request,
        IReadOnlyDictionary<Guid, EventLocationRegistrationAccess> registrationAccess,
        IReadOnlyDictionary<Guid, bool> managementDecisions)
        => request.Purpose switch
        {
            EventLocationDisclosurePurpose.Public => EventLocationDisclosureAuthorityFact.ForPublic(
                request.TenantId,
                request.EventId,
                request.EventLocationId),
            EventLocationDisclosurePurpose.Attendee
                when registrationAccess.TryGetValue(request.EventLocationId, out EventLocationRegistrationAccess? access)
                    && access.CoversRequestedEventLocation => EventLocationDisclosureAuthorityFact.ForAttendee(
                        request.RequesterUserId!.Value,
                        request.TenantId,
                        request.EventId,
                        request.EventLocationId,
                        access),
            EventLocationDisclosurePurpose.Management
                when managementDecisions.GetValueOrDefault(request.EventLocationId) => EventLocationDisclosureAuthorityFact.ForManagement(
                    request.RequesterUserId!.Value,
                    request.TenantId,
                    request.EventId,
                    request.EventLocationId),
            _ => null
        };
}
