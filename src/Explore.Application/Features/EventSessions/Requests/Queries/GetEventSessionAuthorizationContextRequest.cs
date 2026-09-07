using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSession;
using MediatR;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record GetEventSessionAuthorizationContextRequest : IRequest<EventSessionAuthorizationContextDto?>, ISecureRequest
{
    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
