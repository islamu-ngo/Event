using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

public sealed class TenantLifecycleAccessService(ITenantRepository tenants, IAdminContext adminContext)
    : ITenantLifecycleAccessService
{
    public async Task<bool> IsPublicAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId is not { } id || id == Guid.Empty)
            return false;

        var tenant = await tenants.GetByIdAsNoTrackingAsync(id, cancellationToken);
        return TenantLifecycleAccessPolicy.AllowsPublic(tenant?.TenantStatusId);
    }

    public async Task<bool> CanManageAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId is not { } id || id == Guid.Empty)
            return false;

        var tenant = await tenants.GetByIdAsNoTrackingAsync(id, cancellationToken);
        if (tenant is null)
            return false;

        bool instanceAdministrator = await adminContext.IsInstanceAdminAsync(cancellationToken);
        Guid? authorizedTenant = !instanceAdministrator && await adminContext.IsTenantAdminAsync(id, cancellationToken)
            ? id : null;
        return TenantLifecycleAccessPolicy.AllowsManagement(id, tenant.TenantStatusId, authorizedTenant, instanceAdministrator);
    }
}
