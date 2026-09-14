using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Tenants.Requests.Commands;

namespace Explore.Application.Features.Tenants.Handlers.Commands;

public sealed class DeleteTenantCommandHandler(
    ITenantRepository tenantRepository,
    ITenantSlugCache tenantSlugCache)
    : ICommandHandler<DeleteTenantCommand, bool>
{
    public async Task<bool> ExecuteAsync(
        DeleteTenantCommand request,
        CancellationToken cancellationToken = default)
    {
        var tenant = await tenantRepository.GetById(request.Id);
        if (tenant is null)
        {
            return false;
        }

        await tenantRepository.Delete(tenant);
        await tenantSlugCache.RefreshAsync(cancellationToken);
        return true;
    }
}
