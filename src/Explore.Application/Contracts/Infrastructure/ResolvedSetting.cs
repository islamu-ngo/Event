using Explore.Domain;

namespace Explore.Application.Contracts.Infrastructure;

public class ResolvedSetting
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public SettingValueType ValueType { get; init; }
    public SettingSource Source { get; init; }
    public bool IsLocked { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? AllowedValues { get; init; }
}

public enum SettingSource
{
    SystemDefault = 0,
    TenantOverride = 1,
    SystemLocked = 2,
    OrganizationOverride = 3,
    GroupOverride = 4,
    UserPreference = 5,
    TenantLocked = 6
}
