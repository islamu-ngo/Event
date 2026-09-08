using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionGroup, AuthorizationActions.Update)]
public sealed record UpdateEventSessionGroupCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventSessionGroupId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventSessionGroupRequestDto EventSessionGroup { get; init; }

    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionGroupId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(TenantId, EventId);
}
