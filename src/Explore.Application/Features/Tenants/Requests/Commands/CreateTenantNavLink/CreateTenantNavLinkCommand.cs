using System;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;

namespace Explore.Application.Features.Tenants.Requests.Commands.CreateTenantNavLink;

/// <summary>
/// Command to create a new tenant navigation link.
/// Returns the ID of the created navigation link.
/// </summary>
public sealed record CreateTenantNavLinkCommand : ICommand<BaseCommandResponse<Guid>>
{
    /// <summary>
    /// DTO containing the navigation link data to create.
    /// </summary>
    public CreateTenantNavigationLinkDto NavigationLinkDto { get; init; } = null!;
}
