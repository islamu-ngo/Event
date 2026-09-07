namespace Explore.Application.Contracts.Infrastructure;

using Explore.Application.Settings;
using Explore.Domain.Settings;

/// <summary>
/// Resolves settings through a 5-tier hierarchy with batch loading and lock semantics.
/// Instance → Tenant → Organization → Group → User.
/// A lock at any scope prevents child scopes from overriding that setting.
/// </summary>
public interface IHierarchicalSettingsResolver
{
    /// <summary>
    /// Resolves a single setting value, casting to <typeparamref name="T"/>.
    /// Walks the hierarchy from Instance to the deepest scope in <paramref name="context"/>.
    /// </summary>
    Task<T?> ResolveAsync<T>(string key, SettingContext context, CancellationToken ct = default);

    /// <summary>
    /// Resolves a single setting with full metadata (source scope, lock state, definition info).
    /// </summary>
    Task<ResolvedSetting?> ResolveWithMetadataAsync(string key, SettingContext context, CancellationToken ct = default);

    /// <summary>
    /// Batch-resolves multiple settings in minimal DB roundtrips.
    /// Returns one <see cref="ResolvedSetting"/> per key, in the same order as <paramref name="keys"/>.
    /// </summary>
    Task<IReadOnlyList<ResolvedSetting>> ResolveBatchAsync(
        IEnumerable<string> keys, SettingContext context, CancellationToken ct = default);

    /// <summary>
    /// Resolves all settings required by a strongly-typed setting group.
    /// </summary>
    Task<TGroup> ResolveGroupAsync<TGroup>(SettingContext context, CancellationToken ct = default)
        where TGroup : ISettingGroup, new();

    /// <summary>
    /// Sets a value at a specific scope level. Validates against the setting definition's allowed scope range.
    /// </summary>
    Task SetValueAsync(string key, string value, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default);

    /// <summary>
    /// Removes an override at a specific scope, reverting to the parent scope's value.
    /// </summary>
    Task RemoveOverrideAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default);

    /// <summary>
    /// Locks a setting at a specific scope, preventing child scopes from overriding it.
    /// Supported scopes: Instance, Tenant. Lower-scope values remain in storage but become non-effective.
    /// </summary>
    Task LockAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default);

    /// <summary>
    /// Unlocks a setting at a specific scope, restoring the normal cascade.
    /// Supported scopes: Instance, Tenant. Lower-scope values that were suppressed become effective again.
    /// </summary>
    Task UnlockAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached instance, tenant, or group settings.
    /// Pass null for both parameters to invalidate the system cache.
    /// </summary>
    void InvalidateCache(SettingScope? scope = null, Guid? scopeId = null);

    /// <summary>
    /// Invalidates one tenant-specific organization setting cache entry.
    /// </summary>
    void InvalidateOrganizationCache(Guid tenantId, Guid organizationId);

    /// <summary>
    /// Invalidates a specific user preference cache entry.
    /// </summary>
    void InvalidateUserCache(Guid tenantId, Guid userId);
}
