using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;

[AuthorizeResource(ResourceKinds.TenantUserRoleGrant, AuthorizationActions.Delete)]
public sealed record RevokeTenantUserRoleGrantCommand : ICommand<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
