using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;

namespace Explore.Application.Features.Tenants.Requests.Commands;

/// <summary>
/// Command to create a new tenant.
/// Returns the ID of the created tenant.
/// </summary>
[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Create)]
public sealed record CreateTenantCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    /// <summary>
    /// DTO containing the tenant data to create.
    /// </summary>
    public CreateTenantDto TenantDto { get; init; } = null!;

    /// <summary>
    /// The authenticated application user making the request, resolved from the persisted provider binding by the HTTP adapter.
    /// Required when <see cref="CreateTenantDto.AssignCurrentUserAsTenantAdmin"/> is true.
    /// </summary>
    public Guid? RequestingUserId { get; init; }

    string? ISecureRequest.ResourceId => null;
}
