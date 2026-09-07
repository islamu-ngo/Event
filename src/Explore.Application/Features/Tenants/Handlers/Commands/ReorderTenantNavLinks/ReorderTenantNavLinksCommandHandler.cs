using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Tenants.Requests.Commands.ReorderTenantNavLinks;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Commands.ReorderTenantNavLinks;

/// <summary>
/// Handler for ReorderTenantNavLinksCommand.
/// Updates the display order of multiple navigation links for the current tenant.
/// Verifies all links belong to the tenant before updating.
/// </summary>
public class ReorderTenantNavLinksCommandHandler : IRequestHandler<ReorderTenantNavLinksCommand, BaseCommandResponse<bool>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;

    public ReorderTenantNavLinksCommandHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<bool>> Handle(ReorderTenantNavLinksCommand request, CancellationToken cancellationToken)
    {
        if (request.NavigationLinkOrders?.Count == 0)
        {
            return BaseCommandResponse.Validation<bool>(
                ["Navigation link list is empty."],
                "No navigation links provided for reordering.");
        }

        // Get all navigation links for the current tenant
        var allLinks = await _navigationLinkRepository.GetByTenantIdOrderedAsync(
            _tenantContext.TenantId,
            cancellationToken);

        // Verify all requested links exist and belong to the tenant
        var requestedIds = request.NavigationLinkOrders!.Select(x => x.Id).ToList();
        var existingIds = allLinks.Select(x => x.Id).ToList();

        var invalidIds = requestedIds.Where(id => !existingIds.Contains(id)).ToList();
        if (invalidIds.Any())
        {
            return BaseCommandResponse.Validation<bool>(
                [$"Invalid link IDs: {string.Join(", ", invalidIds)}"],
                "One or more navigation links not found or do not belong to your tenant.");
        }

        // Update the order for each navigation link
        foreach (var orderUpdate in request.NavigationLinkOrders)
        {
            var link = allLinks.FirstOrDefault(x => x.Id == orderUpdate.Id);
            if (link != null)
            {
                link.Order = orderUpdate.Order;
                await _navigationLinkRepository.Update(link);
            }
        }

        return BaseCommandResponse.Success(false, "Navigation links reordered successfully.");
    }
}
