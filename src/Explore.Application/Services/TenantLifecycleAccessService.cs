using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

public sealed class TenantLifecycleAccessService(ITenantRepository tenants, IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository)
    : ITenantLifecycleAccessService
{
    public async Task<bool> IsPublicAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId is not { } id || id == Guid.Empty)
            return false;

        var tenant = await tenants.GetByIdAsNoTrackingAsync(id, cancellationToken);
        return TenantLifecycleAccessPolicy.AllowsPublic(tenant?.TenantStatusId);
    }

    public async Task<bool> CanAttemptConfiguredAdministratorSyncAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId != PlatformDefaults.DefaultTenantId)
            return false;

        var tenant = await tenants.GetByIdAsNoTrackingAsync(PlatformDefaults.DefaultTenantId, cancellationToken);
        if (tenant?.TenantStatusId != (int)TenantStatusEnum.Provisioning)
            return false;

        var bootstrap = await bootstrapRepository.GetCurrent(cancellationToken);
        return bootstrap is
        {
            Mode: InstanceBootstrapMode.ConfiguredAdministrator,
            Status: InstanceBootstrapStatus.Pending or InstanceBootstrapStatus.Completed
        };
    }

    public async Task<Guid?> ResolveConfiguredAdministratorTenantAsync(string tenantSlug, CancellationToken cancellationToken = default)
    {
        if (!await CanAttemptConfiguredAdministratorSyncAsync(PlatformDefaults.DefaultTenantId, cancellationToken))
            return null;

        var tenant = await tenants.GetByIdAsNoTrackingAsync(PlatformDefaults.DefaultTenantId, cancellationToken);
        return tenant is not null && string.Equals(tenant.Slug, tenantSlug.Trim(), StringComparison.OrdinalIgnoreCase)
            ? tenant.Id : null;
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
