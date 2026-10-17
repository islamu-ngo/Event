using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed partial class StorageReconciliationOwner
{
    public async Task<bool> IsLocalOrphanAsync(Guid bindingId, string objectKey, CancellationToken cancellationToken)
    {
        await using var global = await globalFactory.CreateDbContextAsync(cancellationToken);
        var native = LocalStorageQuarantineNativeOperations.TryBind(global);
        if (native is not null)
        {
            await native.RequireReadyAsync(cancellationToken);
            return await native.InspectAsync(bindingId, objectKey, cancellationToken);
        }
        var binding = await global.Set<StorageProviderBinding>().AsNoTracking()
            .SingleAsync(row => row.Id == bindingId, cancellationToken);
        string path = LocalStorageQuarantine.ResolvePhysicalPath(binding, objectKey);
        if (!LocalStorageQuarantine.CanEstablishCaseIdentity(path)) return false;
        return !await HasLocalCustodyAsync(global, path, null, cancellationToken);
    }

    public async Task<LocalStorageQuarantine?> AdmitLocalQuarantineAsync(Guid bindingId, string objectKey,
        long observedSize, DateTime observedLastModifiedUtc, DateTime modifiedBeforeUtc,
        string admissionReason, CancellationToken cancellationToken)
    {
        await using var global = await globalFactory.CreateDbContextAsync(cancellationToken);
        var native = LocalStorageQuarantineNativeOperations.TryBind(global);
        if (native is not null)
            await native.RequireReadyAsync(cancellationToken);
        return await new EfCoreUnitOfWork(global).ExecuteSerializableAsync(async ct =>
        {
            if (native is not null)
                return await native.AdmitAsync(bindingId, objectKey, observedSize,
                    observedLastModifiedUtc, modifiedBeforeUtc, admissionReason, ct);
            var binding = await global.Set<StorageProviderBinding>().AsNoTracking()
                .SingleAsync(row => row.Id == bindingId, ct);
            if (!LocalStorageQuarantine.CanEstablishCaseIdentity(LocalStorageQuarantine.ResolvePhysicalPath(binding, objectKey)))
                return null;
            var work = LocalStorageQuarantine.Create(binding, objectKey, observedSize,
                observedLastModifiedUtc, modifiedBeforeUtc, admissionReason, DateTime.UtcNow);
            await using var fence = await RelationalNamedLock.AcquireTransactionAsync(
                global, "local-storage-quarantine:" + work.PhysicalPathHash, ct);
            var existing = await global.Set<LocalStorageQuarantine>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.PhysicalPathHash == work.PhysicalPathHash, ct);
            if (existing is not null)
            {
                RequireSamePhysicalPath(existing, work.CanonicalPhysicalPath);
                return existing;
            }
            if (await HasLocalCustodyAsync(global, work.CanonicalPhysicalPath, null, ct)) return null;
            global.Add(work);
            await global.SaveChangesAsync(ct);
            return work;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<LocalStorageQuarantine>> ClaimLocalQuarantineAsync(
        int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > ILocalStorageQuarantineOwner.MaximumClaims || lease <= TimeSpan.Zero || lease > TimeSpan.FromHours(1))
            throw new ArgumentException("Claim requires a bounded page and a lease of at most one hour.");
        await using var global = await globalFactory.CreateDbContextAsync(cancellationToken);
        var native = LocalStorageQuarantineNativeOperations.TryBind(global);
        if (native is not null)
            await native.RequireReadyAsync(cancellationToken);
        return await new EfCoreUnitOfWork(global).ExecuteSerializableAsync<IReadOnlyList<LocalStorageQuarantine>>(async ct =>
        {
            if (native is not null)
                return await native.ClaimAsync(batchSize, lease, ct);
            DateTime now = DateTime.UtcNow;
            var due = await global.Set<LocalStorageQuarantine>()
                .Where(row => row.State == LocalStorageQuarantineState.Pending && row.NextAttemptAtUtc <= now
                    || row.State == LocalStorageQuarantineState.Processing && row.LeaseExpiresAtUtc <= now)
                .OrderBy(row => row.PhysicalPathHash).ThenBy(row => row.Id).Take(batchSize).ToListAsync(ct);
            var claimed = new List<LocalStorageQuarantine>();
            foreach (var work in due)
            {
                await using var fence = await RelationalNamedLock.AcquireTransactionAsync(
                    global, "local-storage-quarantine:" + work.PhysicalPathHash, ct);
                var binding = await global.Set<StorageProviderBinding>().AsNoTracking()
                    .SingleAsync(row => row.Id == work.ProviderBindingId, ct);
                string path = LocalStorageQuarantine.ResolvePhysicalPath(binding, work.ObjectKey);
                RequireSamePhysicalPath(work, path);
                Guid stamp = Guid.CreateVersion7();
                if (!work.TryClaim(work.ConcurrencyStamp, stamp, now, now.Add(lease))) continue;
                if (!LocalStorageQuarantine.CanEstablishCaseIdentity(path) || await HasLocalCustodyAsync(global, path, work.Id, ct))
                    work.TryBlock(stamp, now);
                else
                    claimed.Add(work);
            }
            await global.SaveChangesAsync(ct);
            return claimed;
        }, cancellationToken);
    }

    public async Task<bool> IsLocalQuarantineClaimCurrentAsync(
        FileStorageQuarantineInput claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.QuarantineId == Guid.Empty || claim.ClaimStamp == Guid.Empty
            || claim.ProviderBindingId == Guid.Empty || claim.ExpectedSize < 0
            || claim.ExpectedLastModifiedUtc == default || claim.ExpectedLastModifiedUtc.Kind != DateTimeKind.Utc
            || claim.LeaseExpiresAtUtc == default || claim.LeaseExpiresAtUtc.Kind != DateTimeKind.Utc
            || claim.LeaseExpiresAtUtc <= DateTime.UtcNow)
            return false;
        await using var global = await globalFactory.CreateDbContextAsync(cancellationToken);
        var native = LocalStorageQuarantineNativeOperations.TryBind(global);
        if (native is not null)
            await native.RequireReadyAsync(cancellationToken);
        return await new EfCoreUnitOfWork(global).ExecuteSerializableAsync(async ct =>
        {
            if (native is not null)
                return await native.ValidateClaimAsync(claim, ct);

            await using var fence = await RelationalNamedLock.AcquireTransactionAsync(
                global, "local-storage-quarantine:" + claim.PhysicalPathHash, ct);
            var work = await global.Set<LocalStorageQuarantine>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == claim.QuarantineId, ct);
            bool Matches(LocalStorageQuarantine? row) => row is not null
                && row.State == LocalStorageQuarantineState.Processing
                && row.ConcurrencyStamp == claim.ClaimStamp && row.LeaseExpiresAtUtc == claim.LeaseExpiresAtUtc
                && row.LeaseExpiresAtUtc > DateTime.UtcNow
                && row.ProviderBindingId == claim.ProviderBindingId
                && string.Equals(row.ObjectKey, claim.ObjectKey, StringComparison.Ordinal)
                && row.ObservedSize == claim.ExpectedSize && row.ObservedLastModifiedUtc == claim.ExpectedLastModifiedUtc
                && string.Equals(row.CanonicalPhysicalPath, claim.CanonicalPhysicalPath, StringComparison.Ordinal)
                && string.Equals(row.PhysicalPathHash, claim.PhysicalPathHash, StringComparison.Ordinal);
            if (!Matches(work)) return false;
            var binding = await global.Set<StorageProviderBinding>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == claim.ProviderBindingId, ct);
            if (binding is null) return false;
            string path = LocalStorageQuarantine.ResolvePhysicalPath(binding, claim.ObjectKey);
            RequireSamePhysicalPath(work!, path);
            if (!LocalStorageQuarantine.CanEstablishCaseIdentity(path)
                || await HasLocalCustodyAsync(global, path, claim.QuarantineId, ct))
                return false;
            // Do not turn an awaited custody inventory into a renewed claim.
            var current = await global.Set<LocalStorageQuarantine>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == claim.QuarantineId, ct);
            return Matches(current);
        }, cancellationToken);
    }

    public Task<bool> CompleteLocalQuarantineAsync(Guid id, Guid claimStamp,
        LocalStorageQuarantineOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome is not (LocalStorageQuarantineOutcome.Moved or LocalStorageQuarantineOutcome.AlreadyQuarantined
            or LocalStorageQuarantineOutcome.AlreadyAbsent))
            throw new ArgumentException("Completion requires a settled physical outcome.");
        return SettleLocalQuarantineAsync(id, claimStamp, outcome, null, cancellationToken);
    }

    public Task<bool> BlockLocalQuarantineAsync(Guid id, Guid claimStamp, CancellationToken cancellationToken) =>
        SettleLocalQuarantineAsync(id, claimStamp, LocalStorageQuarantineOutcome.Blocked, null, cancellationToken);

    public Task<bool> RetryLocalQuarantineAsync(Guid id, Guid claimStamp, DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        if (nextAttemptAtUtc == default || nextAttemptAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Retry requires a non-default UTC timestamp.");
        return SettleLocalQuarantineAsync(id, claimStamp, null, nextAttemptAtUtc, cancellationToken);
    }

    private async Task<bool> SettleLocalQuarantineAsync(Guid id, Guid claimStamp,
        LocalStorageQuarantineOutcome? outcome, DateTime? retry, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || claimStamp == Guid.Empty) throw new ArgumentException("Settlement requires exact custody and claim.");
        await using var global = await globalFactory.CreateDbContextAsync(cancellationToken);
        var native = LocalStorageQuarantineNativeOperations.TryBind(global);
        if (native is not null)
            await native.RequireReadyAsync(cancellationToken);
        return await new EfCoreUnitOfWork(global).ExecuteSerializableAsync(async ct =>
        {
            if (native is not null)
                return await native.SettleAsync(id, claimStamp, outcome, retry, ct);
            var work = await global.Set<LocalStorageQuarantine>().SingleOrDefaultAsync(row => row.Id == id, ct);
            if (work is null) return false;
            DateTime now = DateTime.UtcNow;
            bool changed = retry is { } next ? work.TryRetry(claimStamp, now, next)
                : outcome == LocalStorageQuarantineOutcome.Blocked ? work.TryBlock(claimStamp, now)
                : work.TryComplete(claimStamp, outcome!.Value, now);
            if (changed) await global.SaveChangesAsync(ct);
            return changed;
        }, cancellationToken);
    }

    private static void RequireSamePhysicalPath(LocalStorageQuarantine work, string path)
    {
        if (!string.Equals(work.CanonicalPhysicalPath, path, StringComparison.Ordinal)
            || !string.Equals(work.PhysicalPathHash, LocalStorageQuarantine.HashPhysicalPath(path), StringComparison.Ordinal))
            throw new InvalidOperationException("local_quarantine_physical_identity_conflict");
    }

    private static async Task<bool> HasLocalCustodyAsync(
        ExploreDbContext global, string path, Guid? excludingQuarantine, CancellationToken ct)
    {
        string hash = LocalStorageQuarantine.HashPhysicalPath(path);
        var quarantines = await global.Set<LocalStorageQuarantine>().AsNoTracking()
            .Where(row => row.PhysicalPathHash == hash && row.Id != excludingQuarantine).ToListAsync(ct);
        foreach (var work in quarantines) RequireSamePhysicalPath(work, path);
        if (quarantines.Count != 0) return true;
        // Bindings are retained configuration identities, not a metadata inventory.
        // Page only their root/ID; each applicable root contributes one physical key.
        for (int offset = 0; ; offset += 128)
        {
            var bindings = await global.Set<StorageProviderBinding>().AsNoTracking()
                .Where(row => row.Provider == StorageProviders.Local).OrderBy(row => row.Id)
                .Skip(offset).Take(128).Select(row => new { row.Id, row.LocalRootPath }).ToListAsync(ct);
            foreach (var binding in bindings)
            {
                if (binding.LocalRootPath is null || !Path.IsPathFullyQualified(binding.LocalRootPath)) return true;
                string root;
                try
                {
                    root = LocalStorageQuarantine.CanonicalizePhysicalPath(
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(binding.LocalRootPath)));
                }
                catch (ArgumentException) { return true; }
                if (!LocalStorageQuarantine.CanEstablishCaseIdentity(root)) return true;
                string prefix = root.EndsWith('/') ? root : root + "/";
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string key = path[prefix.Length..];
                bool exists = await LocalStorageCustodyQuery.ExistsAsync(global, binding.Id, key, ct);
                if (exists) return true;
            }
            if (bindings.Count < 128) break;
        }
        return false;
    }

}
