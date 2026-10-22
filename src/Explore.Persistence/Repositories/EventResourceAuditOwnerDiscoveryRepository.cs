using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class EventResourceAuditOwnerDiscoveryRepository(
    IGlobalExploreDbContextFactory globalFactory, PostgresNativeQueueEnrollment? enrollment)
    : IEventResourceAuditOwnerDiscoveryRepository
{
    public async Task<IReadOnlyList<Guid>> GetTenantIdsWithAuditAsync(
        Guid? afterTenantId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, EventResourceAuditRetentionService.TenantPageSize);
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("event_resource_audit_discovery_requires_global_owner");
        return await new EfCoreUnitOfWork(context).ExecuteReadCommittedAsync(
            async token =>
            {
                var contextId = context.ContextId;
                var tenantIdentity = context.TenantContext;
                bool relational = context.Database.IsRelational();
                var connection = relational ? RelationalConnectionIdentity.Capture(context) : null;
                var physical = relational ? context.Database.CurrentTransaction?.GetDbTransaction() : null;
                int? backend = connection?.BackendProcessId;
                bool interrupted = false;
                void Changed(object? sender, StateChangeEventArgs change)
                {
                    if (change.CurrentState != ConnectionState.Open) interrupted = true;
                }
                void RequireOriginal()
                {
                    token.ThrowIfCancellationRequested();
                    context.RequireBoundTenantLease();
                    if (context.ContextId != contextId || !ReferenceEquals(tenantIdentity, context.TenantContext)
                        || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                        || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null
                        || connection is not null && (interrupted || !connection.Matches(context)
                            || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                            || physical is null || !connection.Owns(physical)
                            || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())))
                        throw new UnauthorizedAccessException("event_resource_audit_discovery_owner_changed");
                }
                if (connection is not null) connection.StateChange += Changed;
                try
                {
                    RequireOriginal();
                    await PostgresEventResourceAuditReadPermissions.RequireReadyAsync(context, enrollment, token);
                    RequireOriginal();
                    var audits = context.Set<EventResourceAuditEntry>().IgnoreAutoIncludes().AsNoTracking()
                        .IgnoreTenantFilter(TenantFilterBypassReasons.EventResourceAuditRetention);
                    // Retention also visits inactive tenants. Existence, rather than
                    // an audit join, keeps each tenant once even with many audit rows.
                    var owners = context.Tenants.IgnoreAutoIncludes().AsNoTracking()
                        .Where(tenant => audits.Any(audit => audit.TenantId == tenant.Id));
                    if (afterTenantId is { } cursor)
                        owners = owners.Where(tenant => tenant.Id.CompareTo(cursor) > 0);
                    Guid[] page = await owners.OrderBy(tenant => tenant.Id).Select(tenant => tenant.Id)
                        .Take(limit).ToArrayAsync(token);
                    RequireOriginal();
                    return page;
                }
                finally
                {
                    if (connection is not null) connection.StateChange -= Changed;
                }
            }, cancellationToken);
    }
}
