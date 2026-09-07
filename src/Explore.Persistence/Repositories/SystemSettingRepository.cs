// for system-wide configuration settings with caching support.

namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
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

        return UpsertCoreAsync(setting, cancellationToken);
    }

    public Task<string?> UpsertLockAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(setting.SettingKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return _mutationLock.ExecuteAsync(
            setting.SettingKey,
            async token =>
            {
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
        return previousValue;
    }
}
