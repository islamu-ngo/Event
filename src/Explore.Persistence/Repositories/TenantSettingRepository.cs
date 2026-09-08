namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

public class TenantSettingRepository : ITenantSettingRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly ISettingMutationLock _mutationLock;

    public TenantSettingRepository(ExploreDbContext dbContext, ISettingMutationLock mutationLock)
    {
        _dbContext = dbContext;
        _mutationLock = mutationLock;
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

    public Task SetValueAsync(
        Guid tenantId,
        string key,
        string value,
        CancellationToken cancellationToken = default,
        Guid? actorId = null)
    {
        VisitorAccessSettingMutationGuard.RejectGenericMutation(key);
        EmailDeliverySettingKeys.RejectGenericMutation(key);
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return ExecuteEmailPolicyMutationAsync(tenantId, [key], async token =>
        {
            DateTime now = DateTime.UtcNow;
            int updated = await _dbContext.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .Where(setting => setting.TenantId == tenantId && setting.SettingKey == key)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(setting => setting.Value, value)
                        .SetProperty(setting => setting.UpdatedAt, now)
                        .SetProperty(setting => setting.UpdatedBy, actorId), token);

            if (updated > 0)
                return true;

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
            await _dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken, actorId);
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

    public Task<bool> RemoveOverrideAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default)
    {
        VisitorAccessSettingMutationGuard.RejectGenericMutation(key);
        EmailDeliverySettingKeys.RejectGenericMutation(key);
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return ExecuteEmailPolicyMutationAsync(tenantId, [key], async token =>
        {
            int removed = await _dbContext.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .Where(setting => setting.TenantId == tenantId && setting.SettingKey == key)
                .ExecuteDeleteAsync(token);
            return removed > 0;
        }, cancellationToken);
    }

    public Task<bool> LockAsync(
        Guid tenantId,
        string key,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        VisitorAccessSettingMutationGuard.RejectGenericMutation(key);
        EmailDeliverySettingKeys.RejectGenericMutation(key);
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return ExecuteEmailPolicyMutationAsync(tenantId, [key], async token =>
        {
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
                        .SetProperty(setting => setting.UpdatedBy, actorId), token);
            return updated > 0;
        }, cancellationToken, actorId);
    }

    public Task<bool> UnlockAsync(
        Guid tenantId,
        string key,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        VisitorAccessSettingMutationGuard.RejectGenericMutation(key);
        EmailDeliverySettingKeys.RejectGenericMutation(key);
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return ExecuteEmailPolicyMutationAsync(tenantId, [key], async token =>
        {
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
                        .SetProperty(setting => setting.UpdatedBy, actorId), token);
            return updated > 0;
        }, cancellationToken, actorId);
    }

    public async Task<List<TenantSetting>> GetLockedForTenant(Guid tenantId)
    {
        return await _dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsLocked)
            .ToListAsync();
    }

    public Task UpsertManyForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<TenantSettingOverrideUpsert> overrides,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        foreach (var setting in overrides)
        {
            VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
            EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        }
        if (overrides.Any(overrideValue => PublicationPolicySettingKeys.All.Contains(
                overrideValue.SettingKey,
                StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        if (overrides.Count == 0)
        {
            return Task.CompletedTask;
        }

        string[] keys = overrides.Select(overrideValue => overrideValue.SettingKey).Distinct().ToArray();
        return ExecuteEmailPolicyMutationAsync(tenantId, keys, async token =>
        {
            DetachTrackedSmtpSettings(tenantId, keys);
            DateTime now = DateTime.UtcNow;
            List<TenantSetting> existing = await _dbContext.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .Where(setting => setting.TenantId == tenantId && keys.Contains(setting.SettingKey))
                .ToListAsync(token);

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

            await _dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken, actorId);
    }

    public Task CreateManyForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<TenantSettingOverrideUpsert> overrides,
        Guid? actorId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(overrides);
        foreach (var setting in overrides)
        {
            VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
            EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        }
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

        if (overrides.Count == 0)
        {
            return Task.CompletedTask;
        }

        return ExecuteEmailPolicyMutationAsync(tenantId, overrides.Select(value => value.SettingKey), async token =>
        {
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

            await _dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken, actorId);
    }

    private void DetachTrackedSmtpSettings(Guid tenantId, IEnumerable<string> keys)
    {
        var smtpKeys = keys
            .Where(key => RelationalSettingMutationLock.RequiresEmailDeliveryFence([key]))
            .Select(RelationalSettingMutationLock.NormalizeCanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        if (smtpKeys.Count == 0)
            return;

        var entries = _dbContext.ChangeTracker.Entries<TenantSetting>()
            .Where(entry => entry.Entity.TenantId == tenantId
                && smtpKeys.Contains(RelationalSettingMutationLock.NormalizeCanonicalKey(entry.Entity.SettingKey)))
            .ToArray();
        foreach (var entry in entries)
            entry.State = EntityState.Detached;
    }

    private Task<bool> ExecuteEmailPolicyMutationAsync(Guid tenantId, IEnumerable<string> keys,
        Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken, Guid? actorId = null)
    {
        string[] smtpKeys = keys
            .Where(key => RelationalSettingMutationLock.RequiresEmailDeliveryFence([key]))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return smtpKeys.Length == 0
            ? operation(cancellationToken)
            : _mutationLock.ExecuteManyAsync(smtpKeys, async token =>
            {
                var before = await EmailDeliveryPolicyReader.ReadAsync(_dbContext, tenantId, token);
                bool changed = await operation(token);
                if (changed)
                    await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(_dbContext, tenantId, before, actorId, token);
                return changed;
            }, cancellationToken);
    }
}
