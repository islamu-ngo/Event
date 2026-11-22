using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads detached transport settings through an independently selected ordinary tenant owner.</summary>
public interface IStorageProbeTenantSettingsReader
{
    Task<IReadOnlyList<TenantSetting>> ReadAsync(
        Guid tenantId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);
}
