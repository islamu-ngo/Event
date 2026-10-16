using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationRetentionTenantDiscoveryRepository(IGlobalExploreDbContextFactory globalFactory)
    : IRegistrationRetentionTenantDiscoveryRepository
{
    public async Task<IReadOnlyList<Guid>> GetTenantIdsAsync(
        Guid? afterTenantId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, IRegistrationRetentionTenantDiscoveryRepository.PageSize);
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (!context.Database.IsRelational())
            throw new NotSupportedException("registration_retention_discovery_requires_relational_provider");
        if (context.IsTenantFilterBypassed || context.BindTenantLease() is not null)
            throw new InvalidOperationException("registration_retention_discovery_requires_unbound_global");
        if (!context.Database.IsNpgsql())
        {
            var directory = context.Tenants.AsNoTracking().Where(tenant => tenant.TenantStatus.IsActiveState);
            if (afterTenantId is { } cursor)
                directory = directory.Where(tenant => tenant.Id.CompareTo(cursor) > 0);
            return await directory.OrderBy(tenant => tenant.Id).Select(tenant => tenant.Id)
                .Take(limit).ToArrayAsync(cancellationToken);
        }

        string query = $"SELECT tenant_id AS \"Value\" FROM {PostgresRegistrationRetentionDiscoveryContract.FunctionSql(context)}" +
            "(@after_tenant, @take)";
        return await context.Database.SqlQueryRaw<Guid>(query,
            new NpgsqlParameter("after_tenant", NpgsqlDbType.Uuid) { Value = (object?)afterTenantId ?? DBNull.Value },
            new NpgsqlParameter("take", limit)).ToArrayAsync(cancellationToken);
    }
}
