using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoGovernanceSettingRepository(IGlobalExploreDbContextFactory globalFactory)
    : IAtprotoGovernanceSettingRepository
{
    public async Task<List<TenantSetting>> GetOverridesAsync(
        string key, CancellationToken cancellationToken = default)
    {
        if (key is not (GovernanceSettingKeys.Federation.AtprotoEventsEnabled
            or GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled
            or GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode))
            throw new ArgumentException("ATProto governance requires an enumerated federation setting key.", nameof(key));

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await AtprotoGovernanceSettingQuery.ReadAsync(context, key, cancellationToken);
    }
}
