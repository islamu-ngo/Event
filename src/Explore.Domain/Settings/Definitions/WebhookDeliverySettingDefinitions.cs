using Explore.Domain.Constants;

namespace Explore.Domain.Settings.Definitions;

public static class WebhookDeliverySettingDefinitions
{
    public static readonly SettingDefinition MaxConcurrentDeliveries = new(
        Key: GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveries,
        ValueType: SettingValueType.Integer,
        DefaultValue: "16",
        Category: "WebhookDelivery",
        Description: "Cluster-wide maximum number of concurrently leased Local webhook deliveries.",
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition MaxConcurrentDeliveriesPerTenant = new(
        Key: GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerTenant,
        ValueType: SettingValueType.Integer,
        DefaultValue: "4",
        Category: "WebhookDelivery",
        Description: "Maximum concurrent Local webhook deliveries for one tenant.");

    public static readonly SettingDefinition MaxConcurrentDeliveriesPerEndpoint = new(
        Key: GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerEndpoint,
        ValueType: SettingValueType.Integer,
        DefaultValue: "1",
        Category: "WebhookDelivery",
        Description: "Maximum concurrent Local webhook deliveries for one endpoint.");

    public static readonly SettingDefinition MaxItemsPerTenantPerClaimCycle = new(
        Key: GovernanceSettingKeys.WebhookDelivery.MaxItemsPerTenantPerClaimCycle,
        ValueType: SettingValueType.Integer,
        DefaultValue: "10",
        Category: "WebhookDelivery",
        Description: "Maximum delivery attempts one tenant can receive in a fair claim cycle.");

    public static readonly SettingDefinition MaxAttempts = new(
        Key: GovernanceSettingKeys.WebhookDelivery.MaxAttempts,
        ValueType: SettingValueType.Integer,
        DefaultValue: "8",
        Category: "WebhookDelivery",
        Description: "Maximum Local delivery attempts allowed for one endpoint and message.");

    public static readonly SettingDefinition EndpointTimeoutSeconds = new(
        Key: GovernanceSettingKeys.WebhookDelivery.EndpointTimeoutSeconds,
        ValueType: SettingValueType.Integer,
        DefaultValue: "15",
        Category: "WebhookDelivery",
        Description: "Maximum Local delivery request duration for one endpoint.");

    public static readonly SettingDefinition AutoPauseThreshold = new(
        Key: GovernanceSettingKeys.WebhookDelivery.AutoPauseThreshold,
        ValueType: SettingValueType.Integer,
        DefaultValue: "5",
        Category: "WebhookDelivery",
        Description: "Consecutive endpoint failures that open the circuit and auto-pause Local delivery.");

    public static IReadOnlyList<SettingDefinition> All =>
    [
        MaxConcurrentDeliveries,
        MaxConcurrentDeliveriesPerTenant,
        MaxConcurrentDeliveriesPerEndpoint,
        MaxItemsPerTenantPerClaimCycle,
        MaxAttempts,
        EndpointTimeoutSeconds,
        AutoPauseThreshold
    ];
}
