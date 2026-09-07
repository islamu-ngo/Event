using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Commands;

/// <summary>
/// Command to update an existing tenant.
/// Returns the ID of the updated tenant.
/// </summary>
[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record UpdateTenantCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    /// <summary>
    /// DTO containing the tenant data to update.
    /// </summary>
    public Guid TenantId { get; init; }
    public UpdateTenantDto Update { get; init; } = null!;

    string? ISecureRequest.ResourceId => TenantId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
