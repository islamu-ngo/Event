using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionGroup;

namespace Explore.Application.Features.EventSessionGroups.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventSessionGroupsByEventRequest
    : IQuery<List<EventSessionGroupListDto>>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventSessionGroupDetailRequest
    : IQuery<EventSessionGroupDto?>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
