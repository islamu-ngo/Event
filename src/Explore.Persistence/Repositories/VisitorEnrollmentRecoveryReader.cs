using System.Collections.Immutable;
using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Identity-free discovery on the separately configured Global pool.</summary>
public sealed class VisitorEnrollmentRecoveryReader(
    IGlobalExploreDbContextFactory globalFactory,
    PostgresNativeQueueEnrollment? enrollment)
    : IVisitorEnrollmentRecoveryReader
{
    public async Task<ImmutableArray<VisitorEnrollmentRecoveryCandidate>> ReadCandidatesAsync(
        DateTime nowUtc, int maximumCandidates, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidates, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCandidates, IVisitorEnrollmentRecoveryReader.MaximumCandidates);
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Visitor recovery discovery requires a UTC deadline.", nameof(nowUtc));
        if (nowUtc == DateTime.MinValue || nowUtc == DateTime.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(nowUtc), "Visitor recovery discovery requires a finite deadline.");

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("visitor_recovery_discovery_requires_unbound_global");

        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || context.Database.CurrentTransaction is not null || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("visitor_recovery_discovery_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                RequireOriginal();
                await PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                var candidates = await context.Set<VisitorEnrollmentOperation>()
                    .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorEnrollmentRecoveryDiscovery)
                    .IgnoreAutoIncludes().AsNoTracking()
                    .Where(row => ((row.Stage == VisitorEnrollmentStage.Reserved
                            || row.Stage == VisitorEnrollmentStage.IdentityPrepared) && row.ExpiresAt <= nowUtc)
                        || row.Stage == VisitorEnrollmentStage.PrimaryAdmitted
                        || row.Stage == VisitorEnrollmentStage.Canceled && row.CleanupCompletedAt == null)
                    .OrderBy(row => row.ExpiresAt).ThenBy(row => row.Id)
                    .Take(maximumCandidates)
                    .Select(row => new VisitorEnrollmentRecoveryCandidate(
                        row.Id, row.TenantId, row.Stage, row.Generation, row.ExpiresAt))
                    .ToArrayAsync(cancellationToken);
                RequireOriginal();
                // Reattest login, role and tenant GUC before releasing metadata.
                await PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                return candidates.ToImmutableArray();
            });
        }
        finally
        {
            connection.StateChange -= Changed;
            await context.Database.CloseConnectionAsync();
        }
    }
}
