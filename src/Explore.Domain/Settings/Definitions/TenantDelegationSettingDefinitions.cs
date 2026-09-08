namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class TenantDelegationSettingDefinitions
{
    public static readonly SettingDefinition LockSmtp = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own SMTP settings",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockStorage = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockStorage,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own object storage settings",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockAnalytics = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockAnalytics,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own analytics settings",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockAiAssistant = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own AI assistant integration",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockIntegrations = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockIntegrations,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own integration providers",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockReportingProviders = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockReportingProviders,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own reporting moderation providers",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockTenantOspreyProvider = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockTenantOspreyProvider,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own Osprey reporting provider",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockTenantCoopProvider = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockTenantCoopProvider,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can configure their own Coop reporting provider",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockMcp = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockMcp,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can override MCP adapter runtime enablement",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockMcpLegacySse = new(
        Key: GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "TenantDelegation",
        Description: "Whether tenant administrators can override MCP legacy SSE runtime requests",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        LockSmtp,
        LockStorage,
        LockAnalytics,
        LockAiAssistant,
        LockIntegrations,
        LockReportingProviders,
        LockTenantOspreyProvider,
        LockTenantCoopProvider,
        LockMcp,
        LockMcpLegacySse
    ];
}
