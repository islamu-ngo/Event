using Explore.Application.Authorization;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;

[AuthorizeResource(ResourceKinds.TenantUserRoleGrant, AuthorizationActions.Create)]
public sealed record CreateTenantUserRoleGrantCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateTenantUserRoleGrantDto TenantUserRoleGrantDto { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
