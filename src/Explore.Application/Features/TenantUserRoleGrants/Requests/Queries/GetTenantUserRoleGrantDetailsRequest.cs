using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.TenantUserRoleGrant;

namespace Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;

[AuthorizeResource(ResourceKinds.TenantUserRoleGrant, AuthorizationActions.TenantUserRoleGrants.View)]
public sealed record GetTenantUserRoleGrantDetailsRequest : IQuery<TenantUserRoleGrantDto?>, ISecureRequest
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
