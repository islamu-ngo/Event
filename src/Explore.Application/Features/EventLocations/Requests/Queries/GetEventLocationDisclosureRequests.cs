using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Location;

namespace Explore.Application.Features.EventLocations.Requests.Queries;

public sealed record GetPublicEventLocationsRequest(Guid EventId)
    : IQuery<IReadOnlyList<EventLocationPublicDto>?>;

public sealed record GetAttendeeEventLocationsRequest(Guid EventId)
    : IQuery<IReadOnlyList<EventLocationAttendeeDto>?>;

public sealed record GetManagementEventLocationRequest(Guid EventId, Guid EventLocationId)
    : IQuery<EventLocationManagementDto?>;

/// <summary>
/// Every EventLocation attached to the event, projected for management. The review queue is the
/// remediation-only specialization of this same read.
/// </summary>
[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagementEventLocationsRequest(Guid EventId)
    : IQuery<IReadOnlyList<EventLocationManagementDto>?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString("D");
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetEventLocationReviewQueueRequest(Guid EventId)
    : IQuery<IReadOnlyList<EventLocationManagementDto>?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString("D");
}
