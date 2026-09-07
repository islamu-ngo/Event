using Explore.Application.Authorization;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Commands.UpdateTenantNavLink;

/// <summary>
/// Command to update an existing tenant navigation link.
/// Returns a boolean indicating success or failure.
/// </summary>
[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record UpdateTenantNavLinkCommand : IRequest<BaseCommandResponse<bool>>, ISecureRequest
{
    /// <summary>
    /// DTO containing the updated navigation link data.
    /// </summary>
    public Guid NavigationLinkId { get; init; }
    public Guid TenantId { get; init; }
    public UpdateTenantNavigationLinkDto Update { get; init; } = null!;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
