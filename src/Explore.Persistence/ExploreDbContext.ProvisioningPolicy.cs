using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private async Task<int> SaveWithProvisioningPolicyAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        if (ChangeTracker.Entries<ManagedTenantProvisioningPolicySetting>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("provisioning_policy_direct_tracked_write_forbidden");
        var effects = new Dictionary<string, SystemSetting?>(StringComparer.Ordinal);
        var originals = new Dictionary<string, SystemSetting?>(StringComparer.Ordinal);
        foreach (var entry in ChangeTracker.Entries<SystemSetting>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            string originalKey = entry.Property(row => row.SettingKey).OriginalValue;
            SystemSetting? original = entry.State == EntityState.Added ? null : new SystemSetting
            {
                SettingKey = originalKey,
                Value = entry.Property(row => row.Value).OriginalValue,
                IsLocked = entry.Property(row => row.IsLocked).OriginalValue
            };
            if (entry.State != EntityState.Added && originalKey != entry.Entity.SettingKey
                && ManagedTenantProvisioningPolicyInventory.Keys.Contains(originalKey, StringComparer.Ordinal))
                Add(originalKey, original, null);
            string key = entry.Entity.SettingKey;
            if (ManagedTenantProvisioningPolicyInventory.Keys.Contains(key, StringComparer.Ordinal))
                Add(key, originalKey == key ? original : null, entry.State == EntityState.Deleted ? null : new SystemSetting
                {
                    SettingKey = key,
                    Value = entry.State == EntityState.Added || entry.Property(row => row.Value).IsModified
                        ? entry.Entity.Value : entry.Property(row => row.Value).OriginalValue,
                    IsLocked = entry.State == EntityState.Added || entry.Property(row => row.IsLocked).IsModified
                        ? entry.Entity.IsLocked : entry.Property(row => row.IsLocked).OriginalValue
                });
        }
        if (effects.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        var transaction = Database.CurrentTransaction
            ?? throw new InvalidOperationException("provisioning_policy_original_transaction_required");
        if (!transaction.SupportsSavepoints)
            throw new InvalidOperationException("provisioning_policy_savepoint_required");
        string savepoint = "provisioning_policy_" + Guid.NewGuid().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var (key, original) in originals.OrderBy(effect => effect.Key, StringComparer.Ordinal))
                revisions.Add(key, await ManagedTenantProvisioningPolicyProjection.RequireOriginalSourceAsync(
                    this, key, original, cancellationToken));
            // Keep original states pending until both source and projection writes succeed.
            int saved = await base.SaveChangesAsync(false, cancellationToken);
            foreach (var (key, source) in effects.OrderBy(effect => effect.Key, StringComparer.Ordinal))
                await ManagedTenantProvisioningPolicyProjection.RecordAsync(this, key, source, cancellationToken, revisions[key]);
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            if (acceptAllChangesOnSuccess)
                ChangeTracker.AcceptAllChanges();
            return saved;
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }

        void Add(string key, SystemSetting? original, SystemSetting? effect)
        {
            if (!originals.TryAdd(key, original) || !effects.TryAdd(key, effect))
                throw new InvalidOperationException("provisioning_policy_duplicate_tracked_source");
        }
    }

    /// <summary>Initializes only the complete fixed projection on its installation transaction.</summary>
    internal async Task SaveProvisioningPolicyInitializationAsync(
        IReadOnlyList<ManagedTenantProvisioningPolicySetting> rows, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null || ChangeTracker.HasChanges())
            throw new InvalidOperationException("provisioning_policy_initialization_owner_required");
        ManagedTenantProvisioningPolicyInventory.RequireComplete(rows);
        Set<ManagedTenantProvisioningPolicySetting>().AddRange(rows);
        await base.SaveChangesAsync(true, cancellationToken);
    }
}
