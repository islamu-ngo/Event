using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record UpdateEventSessionCustomPropertyDefinitionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid DefinitionId { get; init; }

    public required UpdateEventSessionCustomPropertyDefinitionDto DefinitionDto { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
