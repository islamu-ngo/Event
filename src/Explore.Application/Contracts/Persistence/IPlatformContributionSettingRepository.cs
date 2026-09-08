using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPlatformContributionSettingRepository
{
    Task<PlatformContributionSetting?> GetActiveAsync(CancellationToken cancellationToken);

    Task AddAsync(PlatformContributionSetting setting, CancellationToken cancellationToken);

    Task UpdateAsync(PlatformContributionSetting setting, CancellationToken cancellationToken);
}
