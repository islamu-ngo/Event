using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Tenants.Requests.Commands;

/// <summary>
/// Command to delete a tenant.
/// Returns true if the tenant was successfully deleted, false if not found.
/// </summary>
// The handler prepares the exact selected-provider decision outside leases and
// the repository reacquires it in its real single-attempt committing owner.
public sealed record DeleteTenantCommand : ICommand<bool>, ISecureRequest
{
    /// <summary>
    /// The ID of the tenant to delete.
    /// </summary>
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
