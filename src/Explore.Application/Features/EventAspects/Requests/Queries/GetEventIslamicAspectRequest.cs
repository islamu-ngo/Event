namespace Explore.Application.Features.EventAspects.Requests.Queries;

using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Request to retrieve the Islamic aspect for a specific event.
/// </summary>
public sealed record GetEventIslamicAspectRequest(Guid EventId) : IQuery<EventIslamicAspectDto?>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventIslamicAspectRequest : IQuery<EventIslamicAspectDto?>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
