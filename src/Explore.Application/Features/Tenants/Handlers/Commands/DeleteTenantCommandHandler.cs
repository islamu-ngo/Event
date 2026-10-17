using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Tenants.Requests.Commands;

namespace Explore.Application.Features.Tenants.Handlers.Commands;

public sealed class DeleteTenantCommandHandler(
    ITenantRepository tenantRepository,
    ITenantSlugCache tenantSlugCache,
    IHumanTenantMutationPreparation preparation)
    : ICommandHandler<DeleteTenantCommand, bool>
{
    public async Task<bool> ExecuteAsync(
        DeleteTenantCommand request,
        CancellationToken cancellationToken = default)
    {
        bool deleted = await preparation.ExecuteDeletionAsync(request, tenantRepository, cancellationToken);
        if (!deleted)
        {
            return false;
        }

        await tenantSlugCache.RefreshAsync(cancellationToken);
        return true;
    }
}
