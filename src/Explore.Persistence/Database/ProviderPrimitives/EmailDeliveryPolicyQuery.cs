using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EmailDeliveryPolicyQuery
{
    internal static async Task<List<TenantSetting>> ReadOverridesAsync(
        ExploreDbContext context, Guid? tenantId, string[] keys, CancellationToken cancellationToken) =>
        !tenantId.HasValue && context.Database.IsNpgsql()
            ? await PostgresEmailDeliveryPolicyContract.ReadAsync(context, cancellationToken)
            : await context.TenantSettingOverrides
                .IgnoreTenantFilter(tenantId.HasValue
                    ? TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate
                    : TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking()
                .Where(setting => (tenantId == null || setting.TenantId == tenantId) && keys.Contains(setting.SettingKey)
                    && context.Tenants.Any(tenant => tenant.Id == setting.TenantId))
                .ToListAsync(cancellationToken);
}
