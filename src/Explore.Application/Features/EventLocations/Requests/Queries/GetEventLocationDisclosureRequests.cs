using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Location;
using MediatR;

namespace Explore.Application.Features.EventLocations.Requests.Queries;

public sealed record GetPublicEventLocationsRequest(Guid EventId)
    : IRequest<IReadOnlyList<EventLocationPublicDto>?>;

public sealed record GetAttendeeEventLocationsRequest(Guid EventId)
    : IRequest<IReadOnlyList<EventLocationAttendeeDto>?>;

public sealed record GetManagementEventLocationRequest(Guid EventId, Guid EventLocationId)
    : IRequest<EventLocationManagementDto?>;

/// <summary>
/// Every EventLocation attached to the event, projected for management. The review queue is the
/// remediation-only specialization of this same read.
/// </summary>
[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagementEventLocationsRequest(Guid EventId)
    : IRequest<IReadOnlyList<EventLocationManagementDto>?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString("D");
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetEventLocationReviewQueueRequest(Guid EventId)
    : IRequest<IReadOnlyList<EventLocationManagementDto>?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString("D");
}
