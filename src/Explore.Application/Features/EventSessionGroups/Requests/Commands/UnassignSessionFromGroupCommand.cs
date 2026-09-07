using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionGroup, AuthorizationActions.Update)]
public sealed record UnassignSessionFromGroupCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventSessionGroupId { get; init; }
    public Guid EventSessionId { get; init; }
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(TenantId, EventId);
}
