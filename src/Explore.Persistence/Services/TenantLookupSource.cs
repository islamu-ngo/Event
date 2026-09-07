using Explore.Application.Contracts.Services;
using Explore.Application.Models.Tenants;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public class TenantLookupSource : ITenantLookupSource
{
    private readonly ExploreDbContext _dbContext;

    public TenantLookupSource(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TenantLookupRecord>> GetTenantLookupsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.TenantStatusId == (int)TenantStatusEnum.Active)
            .Select(tenant => new TenantLookupRecord
            {
                TenantId = tenant.Id,
                Slug = tenant.Slug,
            })
            .ToListAsync(cancellationToken);

        if (tenants.Count == 0)
        {
            return tenants;
        }

        var tenantIds = tenants.Select(tenant => tenant.TenantId).ToArray();

        var domainSettings = await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantLookupCacheWarmup)
            .AsNoTracking()
            .Where(setting => tenantIds.Contains(setting.TenantId)
                && (setting.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain
                    || setting.SettingKey == GovernanceSettingKeys.Domains.TenantCustomDomain))
            .Select(setting => new TenantDomainSetting
            {
                TenantId = setting.TenantId,
                SettingKey = setting.SettingKey,
                Value = setting.Value,
            })
            .ToListAsync(cancellationToken);

        var settingsByTenant = domainSettings
            .GroupBy(setting => setting.TenantId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var tenant in tenants)
        {
            if (!settingsByTenant.TryGetValue(tenant.TenantId, out var tenantSettings))
            {
                continue;
            }

            tenant.Subdomain = GetSettingValue(tenantSettings, GovernanceSettingKeys.Domains.TenantSubdomain);
            tenant.CustomDomain = GetSettingValue(tenantSettings, GovernanceSettingKeys.Domains.TenantCustomDomain);
        }

        return tenants;
    }

    private static string? GetSettingValue(IEnumerable<TenantDomainSetting> settings, string key)
    {
        var rawValue = settings
            .Where(setting => setting.SettingKey == key)
            .Select(setting => setting.Value)
            .FirstOrDefault();

        var value = SettingValueSerializer.DeserializeString(rawValue);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class TenantDomainSetting
    {
        public Guid TenantId { get; init; }

        public required string SettingKey { get; init; }

        public required string Value { get; init; }
    }
}
