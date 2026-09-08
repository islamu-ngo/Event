namespace Explore.Application.Features.EventAspects.Requests.Commands;

using System;
using Explore.Application.Authorization;
using MediatR;

/// <summary>
/// Command to delete the Islamic aspect from an event.
/// </summary>
[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record DeleteEventIslamicAspectCommand : IRequest<bool>, ISecureRequest
{
    /// <summary>
    /// The event ID to remove the Islamic aspect from.
    /// </summary>
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
