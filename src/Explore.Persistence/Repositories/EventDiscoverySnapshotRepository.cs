using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Services.Discovery;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class EventDiscoverySnapshotRepository(ExploreDbContext dbContext)
    : IEventDiscoverySnapshotRepository
{
    private Guid? _transactionId;
    private Guid _tenantId;

    public async Task AcquireFenceAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var transaction = dbContext.Database.CurrentTransaction;
        if (!dbContext.Database.IsRelational() || transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Discovery capture requires a caller-owned serializable transaction.");
        if (_transactionId == transaction.TransactionId)
            throw new InvalidOperationException("Acquire the snapshot budget fence once per transaction.");
        if (dbContext.ChangeTracker.Entries<EventDiscoverySnapshot>().Any(entry => entry.State != EntityState.Unchanged) ||
            dbContext.ChangeTracker.Entries<EventDiscoverySnapshotItem>().Any(entry => entry.State != EntityState.Unchanged) ||
            dbContext.ChangeTracker.Entries<EventDiscoverySnapshotReservation>().Any(entry => entry.State != EntityState.Unchanged))
            throw new InvalidOperationException("Snapshot writes cannot precede the budget fence.");

        await EventDiscoverySnapshotProviderOperations.BootstrapReservationAsync(
            dbContext, tenantId, cancellationToken);
        // This native no-op write is essential even after a successful bootstrap/read.
        // PostgreSQL Serializable must abort stale capacity snapshots, not merely wait on SELECT FOR UPDATE.
        // Order: reservation -> candidate/source work -> terminal epoch -> snapshot rows.
        // Public authority writers never acquire this reservation and no process semaphore participates.
        await RelationalEntityRowFence.AcquireGlobalAsync<EventDiscoverySnapshotReservation>(
            dbContext, tenantId, cancellationToken);
        _tenantId = tenantId;
        _transactionId = transaction.TransactionId;
    }

    public async Task<EventDiscoverySnapshot?> FindReusableAsync(
        Guid tenantId, string criteriaHash, long identityEpoch, long disclosureEpoch,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        RequireUtc(nowUtc);
        if (!HasTenant(tenantId))
            return null;
        return await dbContext.Set<EventDiscoverySnapshot>().AsNoTracking()
            .Where(snapshot => snapshot.TenantId == tenantId && snapshot.CriteriaHash == criteriaHash &&
                snapshot.IdentityEpoch == identityEpoch && snapshot.DisclosureEpoch == disclosureEpoch &&
                snapshot.CreatedAtUtc <= nowUtc && snapshot.ExpiresAtUtc > nowUtc)
            .OrderByDescending(snapshot => snapshot.ExpiresAtUtc).ThenBy(snapshot => snapshot.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<EventDiscoverySnapshot?> CaptureAsync(
        EventDiscoverySnapshot snapshot, EventDiscoveryTraversalLimits limits,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(limits);
        RequireFence(snapshot.TenantId);
        RequireUtc(nowUtc);
        if (snapshot.CreatedAtUtc > nowUtc || snapshot.ExpiresAtUtc <= nowUtc ||
            snapshot.ExpiresAtUtc - snapshot.CreatedAtUtc > limits.Lifetime ||
            snapshot.ItemCount != snapshot.Items.Count || snapshot.Items.Count > limits.MaxIdentities ||
            snapshot.Items.Any(item => item.TenantId != snapshot.TenantId || item.SnapshotId != snapshot.Id))
            throw new ArgumentException("Snapshot membership, tenant, and lifetime must match the capture budget.", nameof(snapshot));

        var reusable = await FindReusableAsync(snapshot.TenantId, snapshot.CriteriaHash,
            snapshot.IdentityEpoch, snapshot.DisclosureEpoch, nowUtc, cancellationToken);
        if (reusable is not null)
            return reusable;

        long liveSnapshots = await dbContext.Set<EventDiscoverySnapshot>().AsNoTracking()
            .LongCountAsync(row => row.TenantId == snapshot.TenantId && row.ExpiresAtUtc > nowUtc, cancellationToken);
        // Expired rows still occupy physical storage. Never sum ItemCount metadata or rely on a purge job.
        long physicalItems = await dbContext.Set<EventDiscoverySnapshotItem>().AsNoTracking()
            .LongCountAsync(row => row.TenantId == snapshot.TenantId, cancellationToken);
        if (!limits.CanCapture(liveSnapshots, physicalItems, snapshot.Items.Count))
            return null;
        // Empty traversals have no item rows. Bound their actual headers independently by the same
        // finite ceiling, so varying empty criteria cannot allocate forever while purge is stopped.
        long physicalSnapshots = await dbContext.Set<EventDiscoverySnapshot>().AsNoTracking()
            .LongCountAsync(row => row.TenantId == snapshot.TenantId, cancellationToken);
        if (physicalSnapshots >= limits.MaxPhysicalItems)
            return null;

        dbContext.Set<EventDiscoverySnapshot>().Add(snapshot);
        await dbContext.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public Task<EventDiscoverySnapshot?> GetAsync(
        Guid tenantId, Guid snapshotId, CancellationToken cancellationToken) =>
        !HasTenant(tenantId)
            ? Task.FromResult<EventDiscoverySnapshot?>(null)
            : dbContext.Set<EventDiscoverySnapshot>().AsNoTracking()
                .SingleOrDefaultAsync(snapshot => snapshot.TenantId == tenantId && snapshot.Id == snapshotId,
                    cancellationToken);

    public async Task<IReadOnlyList<EventDiscoverySnapshotItem>> GetItemsAsync(
        Guid tenantId, Guid snapshotId, long afterOrdinal, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(afterOrdinal, -1);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, EventDiscoveryTraversalLimits.PageSizeCeiling);
        if (!HasTenant(tenantId))
            return [];
        return await dbContext.Set<EventDiscoverySnapshotItem>().AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId && item.Ordinal > afterOrdinal)
            .OrderBy(item => item.Ordinal).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(
        Guid tenantId, DateTime nowUtc, int maxSnapshots, CancellationToken cancellationToken)
    {
        RequireFence(tenantId);
        RequireUtc(nowUtc);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSnapshots, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSnapshots, EventDiscoveryTraversalLimits.PurgeBatchCeiling);
        DateTime expiryCutoff = await EventDiscoverySnapshotProviderOperations.BoundExpiryAsync(
            dbContext, nowUtc, cancellationToken);
        var ids = await dbContext.Set<EventDiscoverySnapshot>().AsNoTracking()
            .Where(snapshot => snapshot.TenantId == tenantId && snapshot.ExpiresAtUtc <= expiryCutoff)
            .OrderBy(snapshot => snapshot.ExpiresAtUtc).ThenBy(snapshot => snapshot.Id)
            .Select(snapshot => snapshot.Id).Take(maxSnapshots).ToListAsync(cancellationToken);
        await dbContext.Set<EventDiscoverySnapshotItem>()
            .Where(item => item.TenantId == tenantId && ids.Contains(item.SnapshotId))
            .ExecuteDeleteAsync(cancellationToken);
        return await dbContext.Set<EventDiscoverySnapshot>()
            .Where(snapshot => snapshot.TenantId == tenantId && ids.Contains(snapshot.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private bool HasTenant(Guid tenantId) =>
        tenantId != Guid.Empty && dbContext.TenantFilterTenantId == tenantId && !dbContext.IsTenantFilterBypassed;

    private void RequireTenant(Guid tenantId)
    {
        if (!HasTenant(tenantId))
            throw new InvalidOperationException("discovery_tenant_unavailable");
    }

    private void RequireFence(Guid tenantId)
    {
        RequireTenant(tenantId);
        if (_tenantId != tenantId || _transactionId is null ||
            _transactionId != dbContext.Database.CurrentTransaction?.TransactionId)
            throw new InvalidOperationException("Snapshot mutation requires its active transaction budget fence.");
    }

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Snapshot operations require UTC time.", nameof(value));
    }
}
