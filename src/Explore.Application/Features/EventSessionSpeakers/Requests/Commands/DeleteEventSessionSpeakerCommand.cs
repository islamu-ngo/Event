using System;
using System.Collections.Generic;
using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record DeleteEventSessionSpeakerCommand : IRequest<bool>, ISecureRequest
{
    public Guid Id { get; init; }
    public Guid EventSessionId { get; init; }
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(TenantId, EventId);
}
