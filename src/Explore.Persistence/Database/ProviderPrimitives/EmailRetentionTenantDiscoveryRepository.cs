using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EmailRetentionTenantDiscoveryRepository(
    IGlobalExploreDbContextFactory globalFactory,
    PostgresNativeQueueEnrollment? enrollment)
    : IEmailRetentionTenantDiscoveryRepository
{
    public async Task<IReadOnlyList<Guid>> GetTenantIdsAsync(
        DateTime cutoffUtc, int maxTenants, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTenants, 1);
        if (cutoffUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Email retention discovery requires a UTC cutoff.", nameof(cutoffUtc));
        if (cutoffUtc == DateTime.MinValue || cutoffUtc == DateTime.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cutoffUtc), "Email retention discovery requires a finite cutoff.");
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("email_retention_discovery_requires_unbound_global");
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
                throw new UnauthorizedAccessException("email_retention_discovery_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                RequireOriginal();
                await PostgresEmailRetentionReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                var page = await context.EmailDispatchOutbox
                    .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                    .IgnoreAutoIncludes().AsNoTracking()
                    .Where(row => !row.IsDeleted && row.ContentRedactedAt == null
                        && ((row.Status == EmailDispatchStatus.Sent && row.SentAt <= cutoffUtc)
                            || (row.Status == EmailDispatchStatus.Skipped && row.LastFailureAt <= cutoffUtc)))
                    .GroupBy(row => row.TenantId)
                    .Select(group => new { TenantId = group.Key, OldestCreatedAt = group.Min(row => row.CreatedAt) })
                    .OrderBy(row => row.OldestCreatedAt).ThenBy(row => row.TenantId)
                    .Take(maxTenants).Select(row => row.TenantId).ToArrayAsync(cancellationToken);
                RequireOriginal();
                return page;
            });
        }
        finally
        {
            connection.StateChange -= Changed;
            await context.Database.CloseConnectionAsync();
        }
    }
}
