namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class IntegrationSettingDefinitions
{
    public static readonly SettingDefinition ListmonkEnabled = new(
        Key: GovernanceSettingKeys.Integrations.Listmonk.Enabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Integrations",
        Description: "Whether Listmonk subscriber synchronization is enabled for this tenant",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ListmonkInstanceUrl = new(
        Key: GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Integrations",
        Description: "Base URL of the tenant Listmonk instance",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ListmonkDefaultListId = new(
        Key: GovernanceSettingKeys.Integrations.Listmonk.DefaultListId,
        ValueType: SettingValueType.Integer,
        DefaultValue: "0",
        Category: "Integrations",
        Description: "Default Listmonk list id for contact-sharing subscribers",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ListmonkPreconfirmSubscriptions = new(
        Key: GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Integrations",
        Description: "Whether consented registrations are pre-confirmed when sent to Listmonk",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ListmonkSyncOnRegistration = new(
        Key: GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Integrations",
        Description: "Whether contact-sharing consent queues a Listmonk sync during registration",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ListmonkApiUsername = new(
        Key: InfrastructureSecretSettingKeys.Integrations.Listmonk.ApiUsername,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Integrations",
        Description: "Listmonk API username secret binding",
        MaxScope: SettingScope.Tenant,
        IsSensitive: true);

    public static readonly SettingDefinition ListmonkApiKey = new(
        Key: InfrastructureSecretSettingKeys.Integrations.Listmonk.ApiKey,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Integrations",
        Description: "Listmonk API token or password secret binding",
        MaxScope: SettingScope.Tenant,
        IsSensitive: true);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        ListmonkEnabled,
        ListmonkInstanceUrl,
        ListmonkDefaultListId,
        ListmonkPreconfirmSubscriptions,
        ListmonkSyncOnRegistration,
        ListmonkApiUsername,
        ListmonkApiKey
    ];
}
