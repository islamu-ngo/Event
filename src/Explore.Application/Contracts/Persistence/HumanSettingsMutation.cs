using Explore.Domain.Settings;

namespace Explore.Application.Contracts.Persistence;

public enum HumanSettingsMutationKind { SetValue, SetLock, Remove }
public enum HumanSettingsOperation { UserSettings, TenantSettings, InstanceSettings, InstanceTenantSettings }

/// <summary>
/// Exact storage effect, not caller identity or permission. Values are the already validated,
/// serialized values the owning handler will persist. Null tenant denotes an instance owner;
/// a User effect additionally binds the original resolved user.
/// </summary>
public sealed record HumanSettingsMutation(HumanSettingsOperation Operation, SettingScope Scope, Guid? TenantId, Guid? UserId,
    string Key, HumanSettingsMutationKind Kind, string? Value = null, bool? IsLocked = null);
