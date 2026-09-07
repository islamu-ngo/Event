using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventLocations.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventLocations.Handlers.Queries;

public sealed class GetPublicEventLocationsRequestHandler(
    IEventRepository events,
    IEventLocationRepository eventLocations,
    IEventLocationDisclosureService disclosureService)
    : IRequestHandler<GetPublicEventLocationsRequest, IReadOnlyList<EventLocationPublicDto>?>
{
    public async Task<IReadOnlyList<EventLocationPublicDto>?> Handle(
        GetPublicEventLocationsRequest request,
        CancellationToken cancellationToken)
    {
        Event? eventEntity = await events.GetById(request.EventId);
        if (eventEntity is null
            || eventEntity.EventStatusId != (int)EventStatusEnum.Published
            || eventEntity.VisibilityTypeId != (int)VisibilityTypeEnum.Public)
        {
            return null;
        }

        if (!await events.IsPubliclyEligibleAsync(eventEntity.TenantId, request.EventId, cancellationToken))
        {
            return null;
        }

        IReadOnlyList<EventLocation> placements = await eventLocations.GetByEventIdAsync(
            request.EventId,
            cancellationToken);
        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await ResolveAsync(
                placements,
                EventLocationDisclosurePurpose.Public,
                disclosureService,
                cancellationToken);

        return placements
            .Select(item => results[item.Id])
            .Where(result => result.State != EventLocationDisclosureState.Hidden)
            .Select(EventLocationPublicDto.FromDisclosureResult)
            .ToArray();
    }

    internal static Task<IReadOnlyDictionary<Guid, EventLocationDisclosureResult>> ResolveAsync(
        IReadOnlyList<EventLocation> placements,
        EventLocationDisclosurePurpose purpose,
        IEventLocationDisclosureService disclosureService,
        CancellationToken cancellationToken)
    {
        if (placements.Count == 0)
        {
            return disclosureService.ResolveManyAsync([], cancellationToken);
        }

        EventLocationDisclosureRequest[] requests = placements
            .Select(item => new EventLocationDisclosureRequest(
                item.TenantId,
                item.EventId,
                item.Id,
                RoomId: null,
                RequesterUserId: null,
                purpose))
            .ToArray();
        return disclosureService.ResolveManyAsync(requests, cancellationToken);
    }
}

public sealed class GetAttendeeEventLocationsRequestHandler(
    IEventRepository events,
    IEventLocationRepository eventLocations,
    IEventLocationDisclosureService disclosureService)
    : IRequestHandler<GetAttendeeEventLocationsRequest, IReadOnlyList<EventLocationAttendeeDto>?>
{
    public async Task<IReadOnlyList<EventLocationAttendeeDto>?> Handle(
        GetAttendeeEventLocationsRequest request,
        CancellationToken cancellationToken)
    {
        if (await events.GetById(request.EventId) is null)
        {
            return null;
        }

        IReadOnlyList<EventLocation> placements = await eventLocations.GetByEventIdAsync(
            request.EventId,
            cancellationToken);
        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await GetPublicEventLocationsRequestHandler.ResolveAsync(
                placements,
                EventLocationDisclosurePurpose.Attendee,
                disclosureService,
                cancellationToken);

        return placements
            .Select(item => results[item.Id])
            .Where(result => result.State != EventLocationDisclosureState.Hidden)
            .Select(EventLocationAttendeeDto.FromDisclosureResult)
            .ToArray();
    }
}

public sealed class GetManagementEventLocationRequestHandler(
    IEventRepository events,
    IEventLocationRepository eventLocations,
    IEventLocationDisclosureService disclosureService)
    : IRequestHandler<GetManagementEventLocationRequest, EventLocationManagementDto?>
{
    public async Task<EventLocationManagementDto?> Handle(
        GetManagementEventLocationRequest request,
        CancellationToken cancellationToken)
    {
        EventLocation? placement = (await eventLocations.GetByIdsAsync(
                [request.EventLocationId],
                cancellationToken))
            .SingleOrDefault(item => item.EventId == request.EventId);
        if (placement is null)
        {
            return null;
        }

        Event? authorizationTarget = (await events.GetAuthorizationTargetsByIdsAsync(
                [request.EventId],
                cancellationToken))
            .SingleOrDefault(item =>
                item.Id == request.EventId && item.TenantId == placement.TenantId);
        if (authorizationTarget is null)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await GetPublicEventLocationsRequestHandler.ResolveAsync(
                [placement],
                EventLocationDisclosurePurpose.Management,
                disclosureService,
                cancellationToken);
        EventLocationDisclosureResult result = results[placement.Id];
        if (result.State == EventLocationDisclosureState.Hidden)
        {
            return null;
        }

        return EventLocationManagementProjection.Map(
            placement,
            result,
            authorizationTarget);
    }
}

public sealed class GetManagementEventLocationsRequestHandler(
    IEventRepository events,
    IEventLocationRepository eventLocations,
    IEventLocationDisclosureService disclosureService)
    : IRequestHandler<GetManagementEventLocationsRequest, IReadOnlyList<EventLocationManagementDto>?>
{
    public Task<IReadOnlyList<EventLocationManagementDto>?> Handle(
        GetManagementEventLocationsRequest request,
        CancellationToken cancellationToken) =>
        EventLocationManagementListProjection.ProjectAsync(
            events,
            eventLocations,
            disclosureService,
            request.EventId,
            static _ => true,
            cancellationToken);
}

public sealed class GetEventLocationReviewQueueRequestHandler(
    IEventRepository events,
    IEventLocationRepository eventLocations,
    IEventLocationDisclosureService disclosureService)
    : IRequestHandler<GetEventLocationReviewQueueRequest, IReadOnlyList<EventLocationManagementDto>?>
{
    public Task<IReadOnlyList<EventLocationManagementDto>?> Handle(
        GetEventLocationReviewQueueRequest request,
        CancellationToken cancellationToken) =>
        EventLocationManagementListProjection.ProjectAsync(
            events,
            eventLocations,
            disclosureService,
            request.EventId,
            static placement => placement.NeedsPrivacyReview,
            cancellationToken);
}

/// <summary>
/// Shared management list projection. The full management list and the remediation queue differ only in
/// which placements they select, so both resolve through one batched disclosure call and one
/// authorization target lookup.
/// </summary>
internal static class EventLocationManagementListProjection
{
    public static async Task<IReadOnlyList<EventLocationManagementDto>?> ProjectAsync(
        IEventRepository events,
        IEventLocationRepository eventLocations,
        IEventLocationDisclosureService disclosureService,
        Guid eventId,
        Func<EventLocation, bool> selector,
        CancellationToken cancellationToken)
    {
        Event? authorizationTarget = (await events.GetAuthorizationTargetsByIdsAsync(
                [eventId],
                cancellationToken))
            .SingleOrDefault(item => item.Id == eventId);
        if (authorizationTarget is null)
        {
            return null;
        }

        EventLocation[] placements = (await eventLocations.GetByEventIdAsync(eventId, cancellationToken))
            .Where(selector)
            .ToArray();
        if (placements.Length == 0)
        {
            return [];
        }

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await GetPublicEventLocationsRequestHandler.ResolveAsync(
                placements,
                EventLocationDisclosurePurpose.Management,
                disclosureService,
                cancellationToken);
        return placements
            .Where(placement => results[placement.Id].State != EventLocationDisclosureState.Hidden)
            .Select(placement => EventLocationManagementProjection.Map(
                placement,
                results[placement.Id],
                authorizationTarget))
            .ToArray();
    }
}

internal static class EventLocationManagementProjection
{
    public static EventLocationManagementDto Map(
        EventLocation placement,
        EventLocationDisclosureResult result,
        Event authorizationTarget)
    {
        var descriptor = ResourceDescriptors.EventAuthorizationTarget;
        var updateAuthorization = new AuthorizationRequest(
            descriptor.Kind,
            descriptor.GetResourceId(authorizationTarget),
            AuthorizationActions.Update,
            Scope: descriptor.GetScope(authorizationTarget),
            Facts: descriptor.GetFacts(authorizationTarget));

        return EventLocationManagementDto.FromDisclosureResult(
            result,
            new EventLocationDisclosurePolicyDto(
                placement.ShowVenueName,
                placement.ShowCity,
                placement.ShowCountry,
                placement.ShowRoomName,
                placement.ShowStreetAddress,
                placement.ShowPostcode,
                placement.ShowCoordinates,
                placement.FullDetailsAudienceId,
                placement.RevealFullDetailsFromUtc),
            placement.NeedsPrivacyReview,
            placement.PolicyVersion,
            placement.ConcurrencyStamp,
            updateAuthorization);
    }
}
