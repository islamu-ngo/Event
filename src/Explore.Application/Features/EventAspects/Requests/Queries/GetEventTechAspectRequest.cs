namespace Explore.Application.Features.EventAspects.Requests.Queries;

using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Request to retrieve the Tech aspect for a specific event.
/// </summary>
public sealed record GetEventTechAspectRequest(Guid EventId) : IQuery<EventTechAspectDto?>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventTechAspectRequest : IQuery<EventTechAspectDto?>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
