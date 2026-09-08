namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class ReportingSettingDefinitions
{
    public static readonly SettingDefinition TenantExternalSyncEnabled = new(
        Key: GovernanceSettingKeys.Reporting.TenantExternalSyncEnabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Reporting",
        Description: "Whether tenant-specific reporting provider targets may participate in external synchronization",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition EnableTenantOspreyProvider = new(
        Key: GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Reporting",
        Description: "Whether this tenant contributes its own Osprey moderation provider target",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition EnableTenantCoopProvider = new(
        Key: GovernanceSettingKeys.Reporting.EnableTenantCoopProvider,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Reporting",
        Description: "Whether this tenant contributes its own Coop review queue provider target",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition OspreyRoutingMode = new(
        Key: GovernanceSettingKeys.Reporting.OspreyRoutingMode,
        ValueType: SettingValueType.String,
        DefaultValue: "\"both\"",
        Category: "Reporting",
        Description: "How Osprey reporting targets are selected for this tenant",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["instance", "tenant", "both"]);

    public static readonly SettingDefinition CoopRoutingMode = new(
        Key: GovernanceSettingKeys.Reporting.CoopRoutingMode,
        ValueType: SettingValueType.String,
        DefaultValue: "\"both\"",
        Category: "Reporting",
        Description: "How Coop reporting targets are selected for this tenant",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["instance", "tenant", "both"]);

    public static readonly SettingDefinition EvidenceMode = new(
        Key: GovernanceSettingKeys.Reporting.EvidenceMode,
        ValueType: SettingValueType.String,
        DefaultValue: "\"MetadataOnly\"",
        Category: "Reporting",
        Description: "Evidence detail level used for tenant reporting provider envelopes",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["MetadataOnly", "SafeSummaryOnly", "ReporterText"]);

    public static readonly SettingDefinition OspreyEndpointUrl = new(
        Key: GovernanceSettingKeys.Reporting.OspreyEndpointUrl,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Reporting",
        Description: "Tenant Osprey moderation endpoint URL",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition OspreyApiKey = new(
        Key: InfrastructureSecretSettingKeys.Reporting.OspreyApiKey,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Reporting",
        Description: "Tenant Osprey moderation API key",
        MaxScope: SettingScope.Tenant,
        IsSensitive: true);

    public static readonly SettingDefinition OspreyWebhookSecret = new(
        Key: InfrastructureSecretSettingKeys.Reporting.OspreyWebhookSecret,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Reporting",
        Description: "Tenant Osprey callback webhook signing secret",
        MaxScope: SettingScope.Tenant,
        IsSensitive: true);

    public static readonly SettingDefinition CoopEndpointUrl = new(
        Key: GovernanceSettingKeys.Reporting.CoopEndpointUrl,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Reporting",
        Description: "Tenant Coop review queue endpoint URL",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition CoopApiKey = new(
        Key: InfrastructureSecretSettingKeys.Reporting.CoopApiKey,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Reporting",
        Description: "Tenant Coop review queue API key",
        MaxScope: SettingScope.Tenant,
        IsSensitive: true);

    public static readonly SettingDefinition CoopWebhookSecret = new(
        Key: InfrastructureSecretSettingKeys.Reporting.CoopWebhookSecret,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Reporting",
        Description: "Tenant Coop callback webhook signing secret",
        MaxScope: SettingScope.Tenant,
        IsSensitive: true);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        TenantExternalSyncEnabled,
        EnableTenantOspreyProvider,
        EnableTenantCoopProvider,
        OspreyRoutingMode,
        CoopRoutingMode,
        EvidenceMode,
        OspreyEndpointUrl,
        OspreyApiKey,
        OspreyWebhookSecret,
        CoopEndpointUrl,
        CoopApiKey,
        CoopWebhookSecret
    ];
}
