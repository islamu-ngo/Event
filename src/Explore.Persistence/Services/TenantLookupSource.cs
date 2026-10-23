using Explore.Application.Contracts.Services;
using Explore.Application.Models.Tenants;
using Explore.Application.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

public class TenantLookupSource : ITenantLookupSource
{
    private readonly IGlobalExploreDbContextFactory _factory;

    public TenantLookupSource(IGlobalExploreDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<Guid?> GetCurrentDomainTenantAsync(
        string domain, bool customDomain, CancellationToken cancellationToken = default)
    {
        var serialized = SettingValueSerializer.Serialize(domain.ToLowerInvariant());
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        var matches = await TenantLookupQuery.FindDomainMatchesAsync(
            dbContext, serialized, customDomain, cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<TenantLookupRecord>> GetTenantLookupsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);
        return await TenantLookupQuery.ReadDirectoryAsync(dbContext, cancellationToken);
    }
}
