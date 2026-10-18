using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>Reads current instance and selected-tenant settings on the original request owner.</summary>
public sealed class InstanceTenantSettingsReader(
    ExploreDbContext context, ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings, IServiceProvider services) : IInstanceTenantSettingsReader
{
    public async Task<(IReadOnlyList<SystemSetting> System, IReadOnlyList<TenantSetting> Tenant)> ReadAsync(
        Guid? tenantId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        if (context.IsConfigurationInstanceImportOwner)
        {
            context.ValidateTenantLease();
            return (await systemSettings.GetAllSettings(cancellationToken: cancellationToken),
                tenantId is { } importTenant
                    ? await tenantSettings.GetByTenantAndKeys(importTenant, keys, cancellationToken) : []);
        }

        var native = await PostgresOrdinarySettingsContract.TryReadHierarchyAsync(context,
            () => services.GetRequiredService<PostgresNativeQueueEnrollment>(),
            tenantId ?? context.TenantFilterTenantId, keys, cancellationToken);
        if (native is { } hierarchy)
            return (hierarchy.System, tenantId.HasValue ? hierarchy.Tenant : []);

        return (await systemSettings.GetAllSettings(cancellationToken: cancellationToken),
            tenantId is { } selected
                ? await tenantSettings.GetAllForTenant(selected, cancellationToken) : []);
    }
}
