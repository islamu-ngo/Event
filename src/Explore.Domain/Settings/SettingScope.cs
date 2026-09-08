namespace Explore.Domain.Settings;

/// <summary>
/// Scope level at which a setting value can be defined or overridden.
/// Lower values are broader scopes; higher values are narrower.
/// The cascade resolves from Instance (broadest) down to User (narrowest),
/// stopping at locked levels.
/// </summary>
public enum SettingScope
{
    /// <summary>Platform-wide default. Stored in SystemSetting table.</summary>
    Instance = 0,

    /// <summary>Tenant-level override. Stored in TenantSetting table.</summary>
    Tenant = 1,

    /// <summary>Organization-level override. Stored in OrganizationSetting table.</summary>
    Organization = 2,

    /// <summary>Group-level override. Stored in GroupSetting table.</summary>
    Group = 3,

    /// <summary>User-level preference. Stored in UserPreference table.</summary>
    User = 4
}
