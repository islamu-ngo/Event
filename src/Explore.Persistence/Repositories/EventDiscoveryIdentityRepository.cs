using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Services.Discovery;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class EventDiscoveryIdentityRepository(ExploreDbContext dbContext)
    : IEventDiscoveryIdentityRepository
{
    private Guid? _transactionId;
    private Guid _tenantId;
    private HashSet<Guid> _fencedIds = [];
    private EventDiscoveryRevision? _revision;

    public async Task AcquireFenceAsync(
        Guid tenantId, IReadOnlyCollection<Guid> identityIds, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var transaction = dbContext.Database.CurrentTransaction;
        if (!dbContext.Database.IsRelational() || transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Discovery identity requires a caller-owned serializable transaction.");
        if (_transactionId == transaction.TransactionId)
            throw new InvalidOperationException("Acquire the complete discovery fence once per transaction.");
        var fenceIds = identityIds.Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        if (fenceIds.Contains(Guid.Empty))
            throw new ArgumentException("Identity fence keys must be nonempty.", nameof(identityIds));

        foreach (var entry in dbContext.ChangeTracker.Entries()
                     .Where(entry => entry.Entity is EventDiscoveryIdentity or EventDiscoveryAlias or EventDiscoveryRevision)
                     .ToArray())
        {
            if (entry.State != EntityState.Unchanged)
                throw new InvalidOperationException("Discovery changes cannot precede their fence.");
            entry.State = EntityState.Detached;
        }

        // The same byte-independent ordering is used for every provider.
        foreach (var id in fenceIds)
        {
            await RelationalNamedLock.AcquireTransactionAsync(
                dbContext, $"discovery-identity:{tenantId:N}:{id:N}", cancellationToken);
            await RelationalEntityRowFence.AcquireAsync<EventDiscoveryIdentity>(
                dbContext, tenantId, identity => identity.Id, id, cancellationToken);
        }

        await RelationalNamedLock.AcquireTransactionAsync(
            dbContext, $"discovery-epoch:{tenantId:N}", cancellationToken);
        // SQLite executes a native UPDATE, even when bootstrapping an absent epoch row.
        // This takes the database writer fence; the process-local named lock is not the proof.
        await RelationalEntityRowFence.AcquireAsync<EventDiscoveryRevision>(
            dbContext, tenantId, revision => revision.TenantId, tenantId, cancellationToken);

        _revision = await dbContext.Set<EventDiscoveryRevision>()
            .SingleOrDefaultAsync(revision => revision.TenantId == tenantId, cancellationToken);
        if (_revision is null)
        {
            _revision = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = tenantId };
            dbContext.Set<EventDiscoveryRevision>().Add(_revision);
        }
        _tenantId = tenantId;
        _fencedIds = fenceIds.ToHashSet();
        _transactionId = transaction.TransactionId;
    }

    public async Task<EventDiscoveryIdentity?> FindAsync(
        Guid tenantId, EventDiscoverySourceKind sourceKind, string sourceKey, CancellationToken cancellationToken)
    {
        if (!HasTenant(tenantId))
            return null;
        var hash = EventDiscoveryIdentity.HashSourceKey(sourceKey);
        var identity = await dbContext.Set<EventDiscoveryIdentity>().AsNoTracking()
            .Include(identity => identity.Alias)
            .SingleOrDefaultAsync(identity => identity.TenantId == tenantId && !identity.IsDeleted &&
                identity.SourceKind == sourceKind && identity.SourceKeyHash == hash, cancellationToken);
        return identity?.SourceKey == sourceKey ? identity : null;
    }

    public async Task<IReadOnlyList<EventDiscoveryIdentity>> GetBindingsAsync(
        Guid tenantId, EventDiscoverySourceKind sourceKind,
        IReadOnlyCollection<string> sourceKeys, CancellationToken cancellationToken)
    {
        if (sourceKeys.Count > 1000)
            throw new ArgumentException("Discovery binding reads are bounded to 1000 source keys.", nameof(sourceKeys));
        if (!HasTenant(tenantId) || sourceKeys.Count == 0)
            return [];
        var exactKeys = sourceKeys.ToHashSet(StringComparer.Ordinal);
        var hashes = exactKeys.Select(EventDiscoveryIdentity.HashSourceKey).ToArray();
        var bindings = await dbContext.Set<EventDiscoveryIdentity>().AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Include(identity => identity.Alias)
                .ThenInclude(alias => alias!.Primary)
                    .ThenInclude(primary => primary.Alias)
            .Where(identity => identity.TenantId == tenantId
                && identity.SourceKind == sourceKind && hashes.Contains(identity.SourceKeyHash))
            .ToListAsync(cancellationToken);
        return bindings.Where(identity => exactKeys.Contains(identity.SourceKey)).ToArray();
    }

    public async Task<EventDiscoveryIdentity> GetOrCreateAsync(
        Guid tenantId, EventDiscoverySourceKind sourceKind, string sourceKey, CancellationToken cancellationToken)
    {
        RequireFence(tenantId);
        var proposed = EventDiscoveryIdentity.Create(tenantId, sourceKind, sourceKey);
        var identity = dbContext.Set<EventDiscoveryIdentity>().Local
            .SingleOrDefault(identity => identity.TenantId == tenantId &&
                identity.SourceKind == sourceKind && identity.SourceKeyHash == proposed.SourceKeyHash)
            ?? await dbContext.Set<EventDiscoveryIdentity>()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .SingleOrDefaultAsync(identity => identity.TenantId == tenantId &&
                    identity.SourceKind == sourceKind && identity.SourceKeyHash == proposed.SourceKeyHash,
                    cancellationToken);
        if (identity is not null)
        {
            if (identity.IsDeleted)
                throw new InvalidOperationException("discovery_source_suppressed");
            if (identity.SourceKey != sourceKey)
                throw new InvalidOperationException("discovery_source_key_conflict");
            return identity;
        }

        dbContext.Set<EventDiscoveryIdentity>().Add(proposed);
        _fencedIds.Add(proposed.Id);
        await dbContext.SaveChangesAsync(cancellationToken);
        return proposed;
    }

    public Task<EventDiscoveryRevision?> GetRevisionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!HasTenant(tenantId))
            return Task.FromResult<EventDiscoveryRevision?>(null);
        if (_transactionId == dbContext.Database.CurrentTransaction?.TransactionId && _tenantId == tenantId)
            return Task.FromResult(_revision);
        return dbContext.Set<EventDiscoveryRevision>().AsNoTracking()
            .SingleOrDefaultAsync(revision => revision.TenantId == tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<EventDiscoveryIdentity>> GetGroupAsync(
        Guid tenantId, Guid identityId, CancellationToken cancellationToken)
    {
        if (!HasTenant(tenantId))
            return [];
        return await LoadGroupAsync(tenantId, identityId, tracking: false, cancellationToken);
    }

    public Task<EventDiscoveryRevision> ReviewAsync(
        Guid tenantId, Guid memberId, Guid primaryId, long expectedRevision,
        Guid reviewerId, string reasonCode, DateTime reviewedAtUtc, CancellationToken cancellationToken) =>
        DecideAsync(tenantId, memberId, primaryId, expectedRevision, reviewerId, reasonCode,
            reviewedAtUtc, reverse: false, cancellationToken);

    public Task<EventDiscoveryRevision> ReverseAsync(
        Guid tenantId, Guid memberId, Guid primaryId, long expectedRevision,
        Guid reviewerId, string reasonCode, DateTime reviewedAtUtc, CancellationToken cancellationToken) =>
        DecideAsync(tenantId, memberId, primaryId, expectedRevision, reviewerId, reasonCode,
            reviewedAtUtc, reverse: true, cancellationToken);

    public async Task<EventDiscoveryRevision> AdvanceDisclosureAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        RequireFence(tenantId);
        _revision!.AdvanceDisclosure();
        await dbContext.SaveChangesAsync(cancellationToken);
        return _revision;
    }

    private async Task<EventDiscoveryRevision> DecideAsync(
        Guid tenantId, Guid memberId, Guid primaryId, long expectedRevision,
        Guid reviewerId, string reasonCode, DateTime reviewedAtUtc, bool reverse, CancellationToken cancellationToken)
    {
        RequireFence(tenantId);
        if (!_fencedIds.Contains(memberId) || !_fencedIds.Contains(primaryId))
            throw new InvalidOperationException("Both decision identities must be included in the fence.");
        var members = await LoadGroupAsync(tenantId, memberId, tracking: true, cancellationToken);
        var primaryMembers = await LoadGroupAsync(tenantId, primaryId, tracking: true, cancellationToken);
        var graph = members.Concat(primaryMembers).DistinctBy(identity => identity.Id).ToArray();
        var before = graph.Where(identity => identity.Alias is not null)
            .ToDictionary(identity => identity.Id, identity => identity.Alias!);
        if (reverse)
            EventDiscoveryIdentityRules.Reverse(_revision!, expectedRevision, graph,
                memberId, primaryId, reviewerId, reasonCode, reviewedAtUtc);
        else
            EventDiscoveryIdentityRules.Review(_revision!, expectedRevision, graph,
                memberId, primaryId, reviewerId, reasonCode, reviewedAtUtc);

        foreach (var identity in graph)
        {
            if (identity.Alias is null && before.TryGetValue(identity.Id, out var removed))
                dbContext.Set<EventDiscoveryAlias>().Remove(removed);
            else if (identity.Alias is not null && !before.ContainsKey(identity.Id))
                dbContext.Set<EventDiscoveryAlias>().Add(identity.Alias);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return _revision!;
    }

    private async Task<List<EventDiscoveryIdentity>> LoadGroupAsync(
        Guid tenantId, Guid identityId, bool tracking, CancellationToken cancellationToken)
    {
        var identities = dbContext.Set<EventDiscoveryIdentity>()
            .Where(identity => identity.TenantId == tenantId && !identity.IsDeleted);
        if (!tracking)
            identities = identities.AsNoTracking();
        var requested = await identities.Include(identity => identity.Alias)
            .SingleOrDefaultAsync(identity => identity.Id == identityId, cancellationToken);
        if (requested is null)
            return [];
        var primaryId = requested.Alias?.PrimaryIdentityId ?? requested.Id;
        return await identities.Include(identity => identity.Alias)
            .Where(identity => identity.Id == primaryId ||
                identity.Alias != null && identity.Alias.TenantId == tenantId &&
                identity.Alias.PrimaryIdentityId == primaryId)
            .OrderBy(identity => identity.Id).ToListAsync(cancellationToken);
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
        if (_revision is null || _tenantId != tenantId || _transactionId is null ||
            _transactionId != dbContext.Database.CurrentTransaction?.TransactionId)
            throw new InvalidOperationException("Discovery mutation requires its active transaction fence.");
    }
}
