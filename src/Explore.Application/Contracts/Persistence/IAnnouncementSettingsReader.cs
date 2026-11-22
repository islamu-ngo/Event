using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Returns a consistent entity image; neither observation grants mutation authority.</summary>
public interface IAnnouncementSettingsReader
{
    Task<SystemSetting?> ReadInstanceSourceAsync(CancellationToken cancellationToken);
    Task<TenantSetting?> ReadTenantSourceAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<TenantSetting?> ReadManagedTenantSourceAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<(SystemSetting? Instance, TenantSetting? Tenant)> ReadPublicAsync(
        Guid tenantId, CancellationToken cancellationToken);

    Task<(Guid TenantId, SystemSetting? Instance, TenantSetting? Tenant, UserPreference? Marker)> ReadSelfAsync(
        CancellationToken cancellationToken);
}
