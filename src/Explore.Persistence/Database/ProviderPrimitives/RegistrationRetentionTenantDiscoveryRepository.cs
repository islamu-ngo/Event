using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationRetentionTenantDiscoveryRepository(
    IGlobalExploreDbContextFactory globalFactory,
    PostgresNativeQueueEnrollment? enrollment)
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
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
                throw new UnauthorizedAccessException("registration_retention_discovery_requires_unbound_global");
            await context.Database.OpenConnectionAsync(cancellationToken);
            var connection = RelationalConnectionIdentity.Capture(context);
            var contextId = context.ContextId;
            int? backend = connection.BackendProcessId;
            bool interrupted = false;
            void Changed(object? sender, System.Data.StateChangeEventArgs change)
            {
                if (change.CurrentState != System.Data.ConnectionState.Open) interrupted = true;
            }
            void RequireOriginal()
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.RequireBoundTenantLease();
                if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                    || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                    || context.Database.CurrentTransaction is not null || interrupted || !connection.Matches(context)
                    || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)
                    throw new UnauthorizedAccessException("registration_retention_discovery_original_owner_changed");
            }
            connection.StateChange += Changed;
            try
            {
                RequireOriginal();
                await PostgresRegistrationRetentionReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                var directory = context.Tenants.IgnoreAutoIncludes().AsNoTracking()
                    .Where(tenant => tenant.TenantStatus.IsActiveState);
                if (afterTenantId is { } cursor)
                    directory = directory.Where(tenant => tenant.Id.CompareTo(cursor) > 0);
                var page = await directory.OrderBy(tenant => tenant.Id).Select(tenant => tenant.Id)
                    .Take(limit).ToArrayAsync(cancellationToken);
                RequireOriginal();
                return page;
            }
            finally
            {
                connection.StateChange -= Changed;
                await context.Database.CloseConnectionAsync();
            }
        });
    }
}
