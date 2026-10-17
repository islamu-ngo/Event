using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class VisitorPolicyAuthorityQueries
{
    internal static IQueryable<TenantSetting> Overrides(ExploreDbContext context) =>
        context.TenantSettingOverrides.FromSqlRaw(
            $"SELECT * FROM {PostgresVisitorPolicyAuthorityContract.OverridesFunctionSql(context)}()");

    internal static Task<List<Guid>> AccountRequiredTenantsAsync(
        ExploreDbContext context, CancellationToken token) =>
        context.Database.SqlQueryRaw<Guid>(
                $"SELECT * FROM {PostgresVisitorPolicyAuthorityContract.ParticipationFunctionSql(context)}()")
            .ToListAsync(token);
}
