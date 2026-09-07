using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Tenants.Requests.Commands.DeleteTenantNavLink;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Commands.DeleteTenantNavLink;

/// <summary>
/// Handler for DeleteTenantNavLinkCommand.
/// Deletes a navigation link for the current tenant.
/// Verifies the link belongs to the tenant before deleting.
/// </summary>
public class DeleteTenantNavLinkCommandHandler : IRequestHandler<DeleteTenantNavLinkCommand, BaseCommandResponse<bool>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;

    public DeleteTenantNavLinkCommandHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<bool>> Handle(DeleteTenantNavLinkCommand request, CancellationToken cancellationToken)
    {
        // Verify the navigation link exists and belongs to the current tenant
        var existingLink = await _navigationLinkRepository.GetByIdAndTenantAsync(
            request.Id,
            _tenantContext.TenantId,
            cancellationToken);

        if (existingLink == null)
        {
            return BaseCommandResponse.Validation<bool>(
                ["Navigation link not found."],
                "Navigation link not found or does not belong to your tenant.");
        }

        // Delete the navigation link
        await _navigationLinkRepository.Delete(existingLink);

        return BaseCommandResponse.Success(false, "Navigation link deleted successfully.");
    }
}
