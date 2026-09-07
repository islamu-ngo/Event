using Explore.Application.Authorization;
using Explore.Application.DTOs.TenantUserRoleGrant;
using MediatR;

namespace Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;

[AuthorizeResource(ResourceKinds.TenantUserRoleGrant, AuthorizationActions.TenantUserRoleGrants.View)]
public sealed record GetTenantUserRoleGrantDetailsRequest : IRequest<TenantUserRoleGrantDto?>, ISecureRequest
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
