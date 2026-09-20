using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionGroups.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionGroup, AuthorizationActions.Update)]
public sealed record UpdateEventSessionGroupCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
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
