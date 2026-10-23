using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace Explore.Persistence.Services;

/// <summary>
/// Maintains the fixed nonsecret image on participating producers' original transaction.
/// Direct database writes that bypass those producers do not maintain this projection.
/// </summary>
internal static class ManagedTenantProvisioningPolicyProjection
{
    /// <summary>Explicit installation only; a partial inventory is never repaired.</summary>
    internal static async Task InitializeAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("provisioning_policy_initialization_pending_changes");
        if (context.Database.CurrentTransaction is not null)
        {
            await InitializeWithinTransactionAsync(context, cancellationToken);
            return;
        }
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var owned = await context.Database.BeginTransactionAsync(cancellationToken);
            await InitializeWithinTransactionAsync(context, cancellationToken);
            await owned.CommitAsync(cancellationToken);
        });
    }

    private static async Task InitializeWithinTransactionAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .ToArrayAsync(cancellationToken);
        if (rows.Length != 0)
            ManagedTenantProvisioningPolicyInventory.RequireComplete(rows);
        else
        {
            var source = await context.SystemSettings.AsNoTracking()
                .Where(row => ManagedTenantProvisioningPolicyInventory.Keys.Contains(row.SettingKey))
                .ToDictionaryAsync(row => row.SettingKey, cancellationToken);
            rows = ManagedTenantProvisioningPolicyInventory.Keys.Select(key =>
            {
                var row = ManagedTenantProvisioningPolicySetting.CreateAbsent(key);
                if (source.TryGetValue(key, out var setting)) row.Project(setting);
                return row;
            }).ToArray();
            try
            {
                await context.SaveProvisioningPolicyInitializationAsync(rows, cancellationToken);
            }
            finally
            {
                // Only this installation attempt's rows are detached. A retried owned
                // transaction must inspect durable coverage, not retain rolled-back inserts.
                foreach (var row in rows)
                    context.Entry(row).State = EntityState.Detached;
            }
        }
    }

    internal static async Task RecordAsync(ExploreDbContext context, string key, SystemSetting? source,
        CancellationToken cancellationToken, long? expectedRevision = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ManagedTenantProvisioningPolicyInventory.Keys.Contains(key, StringComparer.Ordinal))
            return;
        SystemSetting? effect = source is null ? null : new SystemSetting
        {
            SettingKey = source.SettingKey,
            Value = source.Value,
            IsLocked = source.IsLocked
        };
        var requireOriginal = RequireOriginalOwner(context, cancellationToken);
        var row = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.SettingKey == key, cancellationToken)
            ?? throw new InvalidOperationException("provisioning_policy_coverage_missing");
        requireOriginal();
        long revision = row.Revision;
        if (expectedRevision.HasValue && revision != expectedRevision.Value)
            throw new DbUpdateConcurrencyException("provisioning_policy_revision_changed");
        row.Project(effect);
        var original = context.Set<ManagedTenantProvisioningPolicySetting>()
            .Where(item => item.Id == row.Id && item.SettingKey == key && item.Revision == revision);
        int changed = ManagedTenantProvisioningPolicyInventory.ValueKeys.Contains(key, StringComparer.Ordinal)
            ? await original.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsPresent, row.IsPresent)
                .SetProperty(item => item.IsLocked, row.IsLocked)
                .SetProperty(item => item.Value, row.Value)
                .SetProperty(item => item.Revision, row.Revision), cancellationToken)
            : await original.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsPresent, row.IsPresent)
                .SetProperty(item => item.IsLocked, row.IsLocked)
                .SetProperty(item => item.Revision, row.Revision), cancellationToken);
        requireOriginal();
        if (changed != 1)
            throw new DbUpdateConcurrencyException("provisioning_policy_revision_changed");
    }

    /// <summary>Rejects stale tracked originals before any source write, without rereading private settings.</summary>
    internal static async Task<long> RequireOriginalSourceAsync(ExploreDbContext context, string key,
        SystemSetting? original, CancellationToken cancellationToken)
    {
        var requireOriginal = RequireOriginalOwner(context, cancellationToken);
        var row = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.SettingKey == key, cancellationToken)
            ?? throw new InvalidOperationException("provisioning_policy_coverage_missing");
        requireOriginal();
        row.RequireValid();
        if (row.IsPresent != (original is not null)
            || original is not null && (original.SettingKey != key || row.IsLocked != original.IsLocked
                || ManagedTenantProvisioningPolicyInventory.ValueKeys.Contains(key, StringComparer.Ordinal)
                    && row.Value != original.Value))
            throw new InvalidOperationException("provisioning_policy_tracked_source_changed");
        return row.Revision;
    }

    private static Action RequireOriginalOwner(ExploreDbContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RequireBoundTenantLease();
        if (!context.Database.IsRelational() || context.TenantFilterTenantId is not null
            || context.Database.CurrentTransaction is not { } transaction)
            throw new InvalidOperationException("provisioning_policy_original_transaction_required");
        var connection = RelationalConnectionIdentity.Capture(context);
        var physicalTransaction = transaction.GetDbTransaction();
        var contextId = context.ContextId;
        RequireOriginal();
        return RequireOriginal;

        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(transaction.GetDbTransaction(), physicalTransaction)
                || !connection.Matches(context) || !connection.Owns(physicalTransaction)
                || connection.State != ConnectionState.Open)
                throw new InvalidOperationException("provisioning_policy_original_transaction_changed");
        }
    }
}
