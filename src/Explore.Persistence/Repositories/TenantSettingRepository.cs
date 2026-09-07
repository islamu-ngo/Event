namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

public class TenantSettingRepository : ITenantSettingRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantSettingRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantSetting?> GetByTenantAndKey(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SettingKey == key, cancellationToken);
    }

    public async Task<TenantSetting?> GetByDomainHostAsync(
        string normalizedHost,
        CancellationToken cancellationToken = default)
    {
        string normalizedValue = normalizedHost.Trim().TrimEnd('.').ToLowerInvariant();
        string canonicalValue = SettingValueSerializer.Serialize(normalizedValue);
        string canonicalValueWithTrailingDot = SettingValueSerializer.Serialize(normalizedValue + ".");
        TenantSetting? match = await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.ManagedTenantDomainUniqueness)
            .AsNoTracking()
            .Where(setting => setting.SettingKey == "domains.tenant_subdomain"
                || setting.SettingKey == "domains.tenant_custom_domain")
            .FirstOrDefaultAsync(
                setting => setting.Value.ToLower() == canonicalValue
                    || setting.Value.ToLower() == canonicalValueWithTrailingDot,
                cancellationToken);

        if (match is null)
        {
            return null;
        }

        string matchedHost = SettingValueSerializer.DeserializeString(match.Value).Trim().TrimEnd('.');
        return string.Equals(matchedHost, normalizedValue, StringComparison.OrdinalIgnoreCase) ? match : null;
    }

    public async Task SetValueAsync(
        Guid tenantId,
        string key,
        string value,
        CancellationToken cancellationToken = default,
        Guid? actorId = null)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        DateTime now = DateTime.UtcNow;
        int updated = await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(setting => setting.TenantId == tenantId && setting.SettingKey == key)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(setting => setting.Value, value)
                    .SetProperty(setting => setting.UpdatedAt, now)
                    .SetProperty(setting => setting.UpdatedBy, actorId),
                cancellationToken);

        if (updated > 0)
        {
            return;
        }

        _dbContext.TenantSettingOverrides.Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = value,
            IsLocked = false,
            CreatedAt = now,
            CreatedBy = actorId
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<TenantSetting>> GetAllForTenant(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TenantSetting>> GetByTenantAndKeys(
        Guid tenantId,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return [];
        }

        return await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(setting => setting.TenantId == tenantId && keys.Contains(setting.SettingKey))
            .ToListAsync(cancellationToken);
    }

    public Task<List<TenantSetting>> GetByKeyAcrossTenants(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGovernanceResolution)
            .AsNoTracking()
            .Where(setting => setting.SettingKey == key)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RemoveOverrideAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        int removed = await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(setting => setting.TenantId == tenantId && setting.SettingKey == key)
            .ExecuteDeleteAsync(cancellationToken);

        return removed > 0;
    }

    public async Task<bool> LockAsync(
        Guid tenantId,
        string key,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        DateTime now = DateTime.UtcNow;
        int updated = await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(setting => setting.TenantId == tenantId
                && setting.SettingKey == key
                && !setting.IsLocked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(setting => setting.IsLocked, true)
                    .SetProperty(setting => setting.UpdatedAt, now)
                    .SetProperty(setting => setting.UpdatedBy, actorId),
                cancellationToken);

        return updated > 0;
    }

    public async Task<bool> UnlockAsync(
        Guid tenantId,
        string key,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        DateTime now = DateTime.UtcNow;
        int updated = await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(setting => setting.TenantId == tenantId
                && setting.SettingKey == key
                && setting.IsLocked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(setting => setting.IsLocked, false)
                    .SetProperty(setting => setting.UpdatedAt, now)
                    .SetProperty(setting => setting.UpdatedBy, actorId),
                cancellationToken);

        return updated > 0;
    }

    public async Task<List<TenantSetting>> GetLockedForTenant(Guid tenantId)
    {
        return await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsLocked)
            .ToListAsync();
    }

    public async Task UpsertManyForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<TenantSettingOverrideUpsert> overrides,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (overrides.Any(overrideValue => PublicationPolicySettingKeys.All.Contains(
                overrideValue.SettingKey,
                StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        if (overrides.Count == 0)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        string[] keys = overrides.Select(overrideValue => overrideValue.SettingKey).Distinct().ToArray();
        List<TenantSetting> existing = await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(setting => setting.TenantId == tenantId && keys.Contains(setting.SettingKey))
            .ToListAsync(cancellationToken);

        Dictionary<string, TenantSetting> existingByKey = existing.ToDictionary(setting => setting.SettingKey);
        foreach (TenantSettingOverrideUpsert overrideValue in overrides)
        {
            if (existingByKey.TryGetValue(overrideValue.SettingKey, out TenantSetting? setting))
            {
                setting.Value = overrideValue.Value;
                setting.IsLocked = overrideValue.IsLocked;
                setting.UpdatedAt = now;
                setting.UpdatedBy = actorId;
                continue;
            }

            _dbContext.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = overrideValue.SettingKey,
                Value = overrideValue.Value,
                IsLocked = overrideValue.IsLocked,
                CreatedAt = now,
                CreatedBy = actorId
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateManyForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<TenantSettingOverrideUpsert> overrides,
        Guid? actorId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(overrides);
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Setting creation timestamp must use UTC kind.", nameof(occurredAtUtc));
        }

        if (overrides.Any(overrideValue => PublicationPolicySettingKeys.All.Contains(
                overrideValue.SettingKey,
                StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        if (overrides.Select(value => value.SettingKey).Distinct(StringComparer.Ordinal).Count()
            != overrides.Count)
        {
            throw new ArgumentException("Tenant setting keys must be unique.", nameof(overrides));
        }

        foreach (TenantSettingOverrideUpsert overrideValue in overrides)
        {
            _dbContext.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = overrideValue.SettingKey,
                Value = overrideValue.Value,
                IsLocked = overrideValue.IsLocked,
                CreatedAt = occurredAtUtc,
                CreatedBy = actorId
            });
        }

        if (overrides.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
