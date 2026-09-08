using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Commands;

/// <summary>
/// Command to create a new tenant.
/// Returns the ID of the created tenant.
/// </summary>
[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Create)]
public sealed record CreateTenantCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    /// <summary>
    /// DTO containing the tenant data to create.
    /// </summary>
    public CreateTenantDto TenantDto { get; init; } = null!;

    /// <summary>
    /// The authenticated user making the request. Set by the controller from the JWT claims.
    /// Required when <see cref="CreateTenantDto.AssignCurrentUserAsTenantAdmin"/> is true.
    /// </summary>
    public Guid? RequestingUserId { get; init; }

    string? ISecureRequest.ResourceId => null;
}
