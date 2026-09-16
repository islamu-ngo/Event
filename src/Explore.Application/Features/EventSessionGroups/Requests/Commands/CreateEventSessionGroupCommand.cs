using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionGroups.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionGroup, AuthorizationActions.Create)]
public sealed record CreateEventSessionGroupCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventSessionGroupRequestDto EventSessionGroup { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionGroup.EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(TenantId, EventSessionGroup.EventId);
}
