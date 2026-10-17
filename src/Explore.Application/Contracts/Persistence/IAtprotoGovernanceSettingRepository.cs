using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoGovernanceSettingRepository
{
    Task<List<TenantSetting>> GetOverridesAsync(string key, CancellationToken cancellationToken = default);
}
