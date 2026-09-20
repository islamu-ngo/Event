using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record GetEventSessionAuthorizationContextRequest : IQuery<EventSessionAuthorizationContextDto?>, ISecureRequest
{
    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
