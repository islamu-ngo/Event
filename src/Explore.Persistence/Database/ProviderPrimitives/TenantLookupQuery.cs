using Explore.Application.Models.Tenants;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class TenantLookupQuery
{
    internal static async Task<IReadOnlyList<Guid>> FindDomainMatchesAsync(
        ExploreDbContext dbContext, string serialized, bool customDomain, CancellationToken cancellationToken)
    {
        List<Guid> matches;
        if (dbContext.Database.IsNpgsql())
        {
            string directorySql = $"""
                SELECT "TenantId" AS tenant_id, "Slug" AS slug,
                    "Subdomain" AS subdomain, "CustomDomain" AS custom_domain
                FROM {PostgresTenantLookupContract.FunctionSql(dbContext)}()
                """;
            var rows = dbContext.Database.SqlQueryRaw<PublicTenantDirectoryRow>(directorySql);
            matches = await rows.Where(row => customDomain
                    ? row.CustomDomain != null && row.CustomDomain.ToLower() == serialized
                    : row.Subdomain != null && row.Subdomain.ToLower() == serialized)
                .Select(row => row.TenantId).Distinct().Take(2).ToListAsync(cancellationToken);
        }
        else
        {
            var key = customDomain ? GovernanceSettingKeys.Domains.TenantCustomDomain
                : GovernanceSettingKeys.Domains.TenantSubdomain;
            matches = await dbContext.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantLookupCacheWarmup)
                .AsNoTracking()
                .Where(setting => setting.SettingKey == key && setting.Value.ToLower() == serialized
                    && dbContext.Tenants.Any(tenant => tenant.Id == setting.TenantId
                        && tenant.TenantStatusId == (int)TenantStatusEnum.Active))
                .Select(setting => setting.TenantId).Distinct().Take(2).ToListAsync(cancellationToken);
        }
        return matches;
    }

    internal static async Task<IReadOnlyList<TenantLookupRecord>> ReadDirectoryAsync(
        ExploreDbContext dbContext, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            string directorySql = $"""
                SELECT "TenantId" AS tenant_id, "Slug" AS slug,
                    "Subdomain" AS subdomain, "CustomDomain" AS custom_domain
                FROM {PostgresTenantLookupContract.FunctionSql(dbContext)}()
                """;
            var directory = await dbContext.Database.SqlQueryRaw<PublicTenantDirectoryRow>(directorySql)
                .ToListAsync(cancellationToken);
            return directory.Select(row => new TenantLookupRecord
            {
                TenantId = row.TenantId,
                Slug = row.Slug,
                Subdomain = DeserializeDomain(row.Subdomain),
                CustomDomain = DeserializeDomain(row.CustomDomain)
            }).ToList();
        }

        var tenants = await dbContext.Tenants
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

        var domainSettings = await dbContext.TenantSettingOverrides
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

        return DeserializeDomain(rawValue);
    }

    private static string? DeserializeDomain(string? rawValue)
    {
        var value = SettingValueSerializer.DeserializeString(rawValue);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class PublicTenantDirectoryRow
    {
        public Guid TenantId { get; set; }
        public required string Slug { get; set; }
        public string? Subdomain { get; set; }
        public string? CustomDomain { get; set; }
    }

    private sealed class TenantDomainSetting
    {
        public Guid TenantId { get; init; }

        public required string SettingKey { get; init; }

        public required string Value { get; init; }
    }
}
