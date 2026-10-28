using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Accesses PostgreSQL's transaction-local tenant setting; callers retain transaction and erasure authority.</summary>
internal static class PostgresTransactionTenantSetting
{
    internal static async Task<string?> ReadAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
            return null;

        return await context.Database.SqlQueryRaw<string>(
            "SELECT COALESCE(current_setting('app.current_tenant_id', true), '') AS \"Value\"")
            .SingleAsync(cancellationToken);
    }

    internal static async Task SetAsync(
        ExploreDbContext context, string tenant, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName == RelationalNamedLock.PostgreSqlProvider)
            await context.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.current_tenant_id', {0}, true)", [tenant], cancellationToken);
    }
}
