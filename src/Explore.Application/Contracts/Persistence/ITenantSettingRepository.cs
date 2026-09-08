namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public sealed record TenantSettingOverrideUpsert(string SettingKey, string Value, bool IsLocked);

/// <summary>
/// Repository for tenant-specific setting overrides.
/// </summary>
public interface ITenantSettingRepository
{
    /// <summary>
    /// Gets a tenant's override for a specific setting key.
    /// </summary>
    Task<TenantSetting?> GetByTenantAndKey(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default);

    Task<TenantSetting?> GetByDomainHostAsync(
        string normalizedHost,
        CancellationToken cancellationToken = default);

    Task SetValueAsync(
        Guid tenantId,
        string key,
        string value,
        CancellationToken cancellationToken = default,
        Guid? actorId = null);

    /// <summary>
    /// Gets all overrides for a tenant.
    /// </summary>
    Task<List<TenantSetting>> GetAllForTenant(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<List<TenantSetting>> GetByTenantAndKeys(
        Guid tenantId,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);

    Task<List<TenantSetting>> GetByKeyAcrossTenants(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a tenant's override for a specific setting key.
    /// </summary>
    Task<bool> RemoveOverrideAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks a tenant setting, preventing lower-scope overrides from taking effect.
    /// Lower-scope values remain in storage but become non-effective while locked.
    /// </summary>
    /// <returns>True if the setting existed and was locked; false if not found.</returns>
    Task<bool> LockAsync(
        Guid tenantId,
        string key,
        Guid actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlocks a tenant setting, restoring normal cascade resolution.
    /// Lower-scope overrides that were non-effective during lock become active again.
    /// </summary>
    /// <returns>True if the setting existed and was unlocked; false if not found.</returns>
    Task<bool> UnlockAsync(
        Guid tenantId,
        string key,
        Guid actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all locked settings for a tenant.
    /// </summary>
    Task<List<TenantSetting>> GetLockedForTenant(Guid tenantId);

    Task UpsertManyForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<TenantSettingOverrideUpsert> overrides,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task CreateManyForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<TenantSettingOverrideUpsert> overrides,
        Guid? actorId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default);
}
