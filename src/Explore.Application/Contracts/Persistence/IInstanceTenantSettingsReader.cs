using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads current detached hierarchy entities without acquiring mutation authority.</summary>
public interface IInstanceTenantSettingsReader
{
    Task<(IReadOnlyList<SystemSetting> System, IReadOnlyList<TenantSetting> Tenant)> ReadAsync(
        Guid? tenantId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);
}
