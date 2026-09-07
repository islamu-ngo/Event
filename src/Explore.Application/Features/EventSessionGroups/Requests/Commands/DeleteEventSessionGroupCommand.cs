using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionGroup, AuthorizationActions.Delete)]
public sealed record DeleteEventSessionGroupCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(TenantId, EventId);
}
