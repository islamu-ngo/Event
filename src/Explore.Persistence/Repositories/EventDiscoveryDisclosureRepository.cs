using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventDiscoveryDisclosureRepository(ExploreDbContext context)
    : IEventDiscoveryDisclosureRepository
{
    public async Task<EventDiscoveryRevision> AcquireCurrentAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        await FenceTenantAsync(tenantId, cancellationToken);
        return await AcquireCurrentCoreAsync(tenantId, cancellationToken);
    }

    private async Task FenceTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Discovery release requires a tenant and an active relational transaction.");
        await InTenantAsync(context, tenantId, async () =>
        {
            // Serializable existence reads retain shared revision locks on SQL Server
            // and MySQL. Taking the named lock afterwards would reverse native/named
            // order against another finalizer. Fence the parent without reading epochs.
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task<EventDiscoveryRevision> AcquireCurrentCoreAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Discovery release requires a tenant and an active relational transaction.");

        context.DisclosureMutations.Seal();
        await RelationalNamedLock.AcquireTransactionAsync(
            context, $"discovery-epoch:{tenantId:N}", cancellationToken);
        return await InTenantAsync(context, tenantId,
            () => new EventDiscoveryDisclosureProviderOperations(context)
                .AcquireRevisionAsync(tenantId, cancellationToken), cancellationToken);
    }

    public Task AdvanceAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken)
    {
        context.DisclosureMutations.Enlist(tenantIds);
        return context.FlushDisclosureAsync(cancellationToken);
    }

    internal async Task FinalizeAsync(
        IReadOnlyCollection<Guid> tenantIds,
        IReadOnlyDictionary<Guid, (long? Expected, long Advancement)> identityRevisions,
        CancellationToken cancellationToken)
    {
        Guid[] ordered = tenantIds.Union(identityRevisions.Keys)
            .OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        if (ordered.Length > Services.EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants
            || ordered.Contains(Guid.Empty))
            throw new InvalidOperationException("discovery_disclosure_fanout_exceeded");
        var operations = new EventDiscoveryDisclosureProviderOperations(context);
        // Epoch-row creation takes a Tenant FK lock. Acquire every possible parent
        // before the first epoch, never epoch(A) -> new source lock for tenant(B).
        foreach (Guid tenantId in ordered)
            await FenceTenantAsync(tenantId, cancellationToken);
        foreach (Guid tenantId in ordered)
        {
            EventDiscoveryRevision revision = await AcquireCurrentCoreAsync(tenantId, cancellationToken);
            if (identityRevisions.TryGetValue(tenantId, out var identity))
            {
                if (identity.Expected is { } expected && revision.IdentityEpoch != expected)
                    throw new InvalidOperationException("discovery_revision_conflict");
                revision.IdentityEpoch = checked(revision.IdentityEpoch + identity.Advancement);
            }
            if (tenantIds.Contains(tenantId))
                revision.AdvanceDisclosure();
            else if (identity.Advancement == 0)
                continue;
            await InTenantAsync(context, tenantId, async () =>
            {
                int affected = await operations.UpdateRevisionAsync(tenantId, revision, cancellationToken);
                if (affected != 1)
                    throw new InvalidOperationException("discovery_disclosure_authority_unavailable");
                return true;
            }, cancellationToken);
            foreach (var entry in context.ChangeTracker.Entries<EventDiscoveryRevision>()
                         .Where(entry => entry.Entity.TenantId == tenantId).ToArray())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    internal static Task<T> InTenantAsync<T>(
        ExploreDbContext context, Guid tenantId, Func<Task<T>> operation, CancellationToken cancellationToken) =>
        EventDiscoveryDisclosureProviderOperations.InTenantAsync(context, tenantId, operation, cancellationToken);
}
