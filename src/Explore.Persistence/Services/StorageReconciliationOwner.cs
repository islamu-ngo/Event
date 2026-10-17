using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>Independent, purpose-bound custody for metadata and local orphan reconciliation.</summary>
public sealed partial class StorageReconciliationOwner(
    IGlobalExploreDbContextFactory globalFactory, PostgresNativeQueueEnrollment? enrollment = null)
    : IStorageReconciliationOwner, ILocalStorageQuarantineOwner
{
    public async Task<IReadOnlyList<StorageReconciliationSnapshot>> ListMissingBackingCandidatesAsync(
        DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        RequireUtc(createdBeforeUtc);
        if (limit is < 1 or > IStorageReconciliationOwner.MaximumCandidates)
            throw new ArgumentOutOfRangeException(nameof(limit));
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        var native = StorageReconciliationProviderOperations.TryBindNative(context, enrollment);
        if (native is not null)
            return await native.ListAsync(createdBeforeUtc, limit, cancellationToken);
        return await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async ct =>
        {
            var sources = await StorageReconciliationProviderOperations.EligibleSources(context)
                .Where(row => row.CreatedAt <= createdBeforeUtc)
                .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Take(limit).ToArrayAsync(ct);
            var result = new List<StorageReconciliationSnapshot>();
            foreach (var source in sources)
                if (await HasSettledTargetAsync(context, source, createdBeforeUtc, ct))
                    result.Add(Capture(source, createdBeforeUtc));
            return (IReadOnlyList<StorageReconciliationSnapshot>)result;
        }, cancellationToken);
    }

    public async Task<bool> TryQuarantineMissingBackingAsync(
        StorageReconciliationSnapshot snapshot, DateTime utcNow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireUtc(utcNow);
        RequireUtc(snapshot.EligibilityCutoffUtc);
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async ct =>
        {
            var native = StorageReconciliationProviderOperations.TryBindNative(context, enrollment);
            if (native is not null)
                return await native.TryQuarantineAsync(snapshot, utcNow, ct);

            // Parent precedes source. The caller's snapshot, never a reloaded replacement,
            // determines which tenant is fenced and which provider absence is consumed.
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, snapshot.TenantId, ct);
            await RelationalEntityRowFence.AcquireGlobalAsync<StorageObject>(context, snapshot.Id, ct);
            var source = await StorageReconciliationProviderOperations.EligibleSources(context)
                .SingleOrDefaultAsync(row => row.Id == snapshot.Id && row.TenantId == snapshot.TenantId, ct);
            if (source is null || source.CreatedAt > snapshot.EligibilityCutoffUtc
                || Capture(source, snapshot.EligibilityCutoffUtc) != snapshot
                || !await HasSettledTargetAsync(context, source, utcNow, ct))
                return false;

            // The common attachment/retirement CAS is acquired only after exact
            // original identity and current eligibility agree under the row fence.
            var fenced = (await context.FenceStorageObjectsAsync([snapshot.Id], ct)).Single();
            int changed = await context.StorageObjects
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(row => row.Id == snapshot.Id && row.TenantId == snapshot.TenantId
                    && row.ConcurrencyStamp == fenced.ConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.LifecycleState, StorageObjectLifecycleStates.Quarantined)
                    .SetProperty(row => row.QuarantinedAt, utcNow)
                    .SetProperty(row => row.QuarantinedBy, (Guid?)null)
                    .SetProperty(row => row.QuarantineReason, "backing_object_missing")
                    .SetProperty(row => row.UpdatedAt, utcNow)
                    .SetProperty(row => row.UpdatedBy, (Guid?)null), ct);
            if (changed != 1)
                throw new DbUpdateConcurrencyException("Storage reconciliation lost its exact source fence.");
            context.DisclosureMutations.Enlist([snapshot.TenantId]);
            await context.FlushDisclosureAsync(ct);
            return true;
        }, cancellationToken);
    }

    private static async Task<bool> HasSettledTargetAsync(
        ExploreDbContext context, StorageObject source, DateTime utcNow, CancellationToken ct)
    {
        var sessions = await context.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking().Where(row => row.StorageObjectId == source.Id || row.Id == source.Id)
            .Take(2).ToArrayAsync(ct);
        var producer = await context.Set<StorageProducerOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.Id, ct);
        if (sessions.Any(session => session.Purpose != source.Purpose
            || session.OwningResourceKind != source.OwningResourceKind
            || session.OwningResourceId != source.OwningResourceId))
            return false;
        StorageObjectDeletionTombstone target;
        try { target = StorageRetirementTarget.Capture(source, sessions, producer, utcNow); }
        catch (ArgumentException) { return false; }
        return target.State == StorageObjectDeletionState.Ready && target.ProviderObjectVersion == source.ProviderVersionId
            && await context.StorageProviderBindings.AsNoTracking()
                .AnyAsync(binding => binding.Id == target.ProviderBindingId && binding.Provider == target.Provider, ct);
    }

    private static StorageReconciliationSnapshot Capture(StorageObject row, DateTime cutoff) => new(
        row.Id, row.ConcurrencyStamp, row.TenantId, row.Provider, row.StorageProviderBindingId!.Value,
        row.ObjectKey!, row.ProviderVersionId, row.LifecycleState, row.IsDeleted, row.DeletedAt,
        row.CreatedAt, row.Purpose, row.OwningResourceKind, row.OwningResourceId, row.ActorId, cutoff);

    private static void RequireUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Storage reconciliation requires a non-default UTC instant.", nameof(value));
    }
}
