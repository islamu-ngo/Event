namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

public class SystemSettingRepository : ISystemSettingRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly ISettingMutationLock _mutationLock;

    public SystemSettingRepository(
        ExploreDbContext dbContext,
        ISettingMutationLock mutationLock)
    {
        _dbContext = dbContext;
        _mutationLock = mutationLock;
    }

    public async Task<SystemSetting?> GetByKey(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken);
    }

    public Task<string?> UpsertAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        if (PublicationPolicySettingKeys.All.Contains(setting.SettingKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return _mutationLock.ExecuteAsync(
            setting.SettingKey,
            token => UpsertCoreAsync(setting, token),
            cancellationToken);
    }

    public Task<string?> UpsertInCurrentTransactionAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Caller-owned system-setting writes require an active transaction.");
        }

        if (PublicationPolicySettingKeys.All.Contains(
                setting.SettingKey,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Guarded publication-policy settings require coordinated mutation.");
        }

        return RelationalSettingMutationLock.RequiresEmailDeliveryFence([setting.SettingKey])
            ? _mutationLock.ExecuteAsync(setting.SettingKey,
                token => UpsertCoreAsync(setting, token), cancellationToken)
            : UpsertCoreAsync(setting, cancellationToken);
    }

    public Task<string?> UpsertLockAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        if (PublicationPolicySettingKeys.All.Contains(setting.SettingKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return _mutationLock.ExecuteAsync(
            setting.SettingKey,
            async token =>
            {
                var policyBefore = RelationalSettingMutationLock.RequiresEmailDeliveryFence([setting.SettingKey])
                    ? await EmailDeliveryPolicyReader.ReadAllAsync(_dbContext, token)
                    : null;
                DetachTrackedSmtpSetting(setting.SettingKey);
                SystemSetting? existing = await _dbContext.SystemSettings
                    .FirstOrDefaultAsync(candidate => candidate.SettingKey == setting.SettingKey, token);
                string? previousValue = existing?.Value;

                if (existing is null)
                {
                    _dbContext.SystemSettings.Add(setting);
                }
                else
                {
                    existing.IsLocked = setting.IsLocked;
                    existing.UpdatedAt = setting.UpdatedAt ?? DateTime.UtcNow;
                    existing.UpdatedBy = setting.UpdatedBy;
                }

                await _dbContext.SaveChangesAsync(token);
                if (policyBefore is not null)
                    await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(_dbContext, policyBefore,
                        setting.UpdatedBy ?? setting.CreatedBy, token);
                return previousValue;
            },
            cancellationToken);
    }

    public async Task<List<SystemSetting>> GetAllSettings(
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SystemSettings.AsNoTracking();

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(s => s.Category == category);
        }

        return await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsLocked(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken);

        return setting?.IsLocked ?? false;
    }

    private async Task<string?> UpsertCoreAsync(
        SystemSetting setting,
        CancellationToken cancellationToken)
    {
        var policyBefore = RelationalSettingMutationLock.RequiresEmailDeliveryFence([setting.SettingKey])
            ? await EmailDeliveryPolicyReader.ReadAllAsync(_dbContext, cancellationToken)
            : null;
        DetachTrackedSmtpSetting(setting.SettingKey);
        SystemSetting? existing = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(
                candidate => candidate.SettingKey == setting.SettingKey,
                cancellationToken);
        string? previousValue = existing?.Value;

        if (existing is null)
        {
            _dbContext.SystemSettings.Add(setting);
        }
        else
        {
            existing.Value = setting.Value;
            existing.ValueType = setting.ValueType;
            existing.IsLocked = setting.IsLocked;
            existing.AllowedValues = setting.AllowedValues;
            existing.Description = setting.Description;
            existing.Category = setting.Category;
            existing.DisplayOrder = setting.DisplayOrder;
            existing.UpdatedAt = setting.UpdatedAt ?? DateTime.UtcNow;
            existing.UpdatedBy = setting.UpdatedBy;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (policyBefore is not null)
            await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(_dbContext, policyBefore,
                setting.UpdatedBy ?? setting.CreatedBy, cancellationToken);
        return previousValue;
    }

    private void DetachTrackedSmtpSetting(string key)
    {
        if (!RelationalSettingMutationLock.RequiresEmailDeliveryFence([key]))
            return;

        // The policy lock protects the next read, but EF's identity map can still contain
        // an earlier transaction's row. Detaching preserves a same-instance request's values.
        string canonicalKey = RelationalSettingMutationLock.NormalizeCanonicalKey(key);
        var entries = _dbContext.ChangeTracker.Entries<SystemSetting>()
            .Where(entry => RelationalSettingMutationLock.NormalizeCanonicalKey(entry.Entity.SettingKey) == canonicalKey)
            .ToArray();
        foreach (var entry in entries)
            entry.State = EntityState.Detached;
    }
}
