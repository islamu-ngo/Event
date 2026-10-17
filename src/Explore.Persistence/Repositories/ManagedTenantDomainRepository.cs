using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class ManagedTenantDomainRepository(IGlobalExploreDbContextFactory factory) : IManagedTenantDomainRepository
{
    public async Task<TenantSetting?> GetByDomainHostAsync(string normalizedHost, CancellationToken cancellationToken = default)
    {
        string normalizedValue = normalizedHost.Trim().TrimEnd('.').ToLowerInvariant();
        string serializedValue = SettingValueSerializer.Serialize(normalizedValue);
        string serializedValueWithTrailingDot = SettingValueSerializer.Serialize(normalizedValue + ".");
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        TenantSetting? match = await ManagedTenantDomainQuery.ReadAsync(
            context, serializedValue, serializedValueWithTrailingDot, cancellationToken);
        if (match is null)
            return null;
        string matchedHost = SettingValueSerializer.DeserializeString(match.Value).Trim().TrimEnd('.');
        return string.Equals(matchedHost, normalizedValue, StringComparison.OrdinalIgnoreCase) ? match : null;
    }
}
