namespace Explore.Application.Contracts.Infrastructure;

using Explore.Domain.Settings;

/// <summary>
/// Resolves settings through a 5-tier hierarchy with batch loading and lock semantics.
/// Instance → Tenant → Organization → Group → User.
/// A lock at any scope prevents child scopes from overriding that setting.
/// </summary>
public interface IHierarchicalSettingsResolver : IHierarchicalSettingsReader
{
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

}
