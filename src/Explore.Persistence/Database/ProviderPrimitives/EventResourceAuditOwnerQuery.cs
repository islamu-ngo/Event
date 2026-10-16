using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventResourceAuditOwnerQuery
{
    internal static async Task<Guid[]> ReadAsync(
        ExploreDbContext context, Guid? afterTenantId, int limit, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
        {
            string query = $"SELECT tenant_id AS \"Value\" FROM {PostgresEventResourceAuditRetentionContract.FunctionSql(context)}" +
                "(@after_tenant, @take)";
            return await context.Database.SqlQueryRaw<Guid>(query,
                    new NpgsqlParameter("after_tenant", NpgsqlDbType.Uuid)
                    {
                        Value = (object?)afterTenantId ?? DBNull.Value
                    },
                    new NpgsqlParameter("take", limit))
                .ToArrayAsync(token);
        }
        var audits = context.Set<EventResourceAuditEntry>()
            .IgnoreAllFilters(TenantFilterBypassReasons.EventResourceAuditRetention);
        var tenants = context.Tenants.IgnoreAllFilters(TenantFilterBypassReasons.EventResourceAuditRetention)
            .AsNoTracking().Where(tenant => audits.Any(audit => audit.TenantId == tenant.Id));
        if (afterTenantId is { } cursor)
            tenants = tenants.Where(tenant => tenant.Id.CompareTo(cursor) > 0);
        return await tenants.OrderBy(tenant => tenant.Id).Select(tenant => tenant.Id).Take(limit).ToArrayAsync(token);
    }
}
