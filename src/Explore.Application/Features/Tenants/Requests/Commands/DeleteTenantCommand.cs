using System;
using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Commands;

/// <summary>
/// Command to delete a tenant.
/// Returns true if the tenant was successfully deleted, false if not found.
/// </summary>
[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Delete)]
public sealed record DeleteTenantCommand : IRequest<bool>, ISecureRequest
{
    /// <summary>
    /// The ID of the tenant to delete.
    /// </summary>
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
