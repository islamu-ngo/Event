using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class AtprotoGovernanceSettingQuery
{
    internal static async Task<List<TenantSetting>> ReadAsync(
        ExploreDbContext context, string key, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
        {
            string command = $"SELECT * FROM {PostgresAtprotoGovernanceContract.FunctionSql(context)}({{0}})";
            return await context.TenantSettingOverrides.FromSqlRaw(command, key)
                .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGovernanceResolution)
                .AsNoTracking().ToListAsync(cancellationToken);
        }

        return await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGovernanceResolution)
            .AsNoTracking().Where(setting => setting.SettingKey == key).ToListAsync(cancellationToken);
    }
}
