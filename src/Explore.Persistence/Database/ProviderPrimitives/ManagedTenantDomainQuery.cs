using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class ManagedTenantDomainQuery
{
    internal static async Task<TenantSetting?> ReadAsync(
        ExploreDbContext context, string serializedValue, string serializedValueWithTrailingDot,
        CancellationToken cancellationToken)
    {
        IQueryable<TenantSetting> query;
        if (context.Database.IsNpgsql())
        {
            string sql = $"SELECT * FROM {PostgresManagedTenantDomainContract.FunctionSql(context)}({{0}}, {{1}})";
            query = context.TenantSettingOverrides.FromSqlRaw(sql, serializedValue, serializedValueWithTrailingDot)
                .IgnoreTenantFilter(TenantFilterBypassReasons.ManagedTenantDomainUniqueness);
        }
        else
        {
            query = context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.ManagedTenantDomainUniqueness)
                .Where(setting => setting.SettingKey == "domains.tenant_subdomain"
                    || setting.SettingKey == "domains.tenant_custom_domain")
                .Where(setting => setting.Value.ToLower() == serializedValue
                    || setting.Value.ToLower() == serializedValueWithTrailingDot);
        }
        return await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
    }
}
