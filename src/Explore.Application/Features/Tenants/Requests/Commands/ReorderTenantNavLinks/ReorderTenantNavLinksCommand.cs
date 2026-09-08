using System.Collections.Generic;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Commands.ReorderTenantNavLinks;

/// <summary>
/// Command to reorder multiple tenant navigation links.
/// Accepts a list of navigation link IDs with their new order values.
/// Returns a boolean indicating success or failure.
/// </summary>
public sealed record ReorderTenantNavLinksCommand : IRequest<BaseCommandResponse<bool>>
{
    /// <summary>
    /// List of navigation links with their new order values.
    /// </summary>
    private IReadOnlyList<UpdateTenantNavigationLinkOrderDto> _navigationLinkOrders =
        Array.AsReadOnly(Array.Empty<UpdateTenantNavigationLinkOrderDto>());

    public IReadOnlyList<UpdateTenantNavigationLinkOrderDto> NavigationLinkOrders
    {
        get => _navigationLinkOrders;
        init => _navigationLinkOrders = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}
