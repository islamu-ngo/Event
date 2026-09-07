namespace Explore.Domain.Settings.Definitions;

public static class CustomPropertyQuotaSettingDefinitions
{
    private const string Category = "CustomPropertyQuotas";

    public static readonly SettingDefinition MaxDefinitionsPerTenantPerEntityScope = new(
        Key: "custom_properties.max_definitions_per_tenant_per_entity_scope",
        ValueType: SettingValueType.Integer,
        DefaultValue: "500",
        Category: Category,
        Description: "Maximum custom-property definitions per tenant per entity scope (Organization/Group/Event/EventSession). Platform max: 5000.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxDefinitionsPerEvent = new(
        Key: "custom_properties.max_definitions_per_event",
        ValueType: SettingValueType.Integer,
        DefaultValue: "100",
        Category: Category,
        Description: "Maximum runtime custom-property definitions attached to a single Event. Platform max: 1000.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxDefinitionsPerEventSession = new(
        Key: "custom_properties.max_definitions_per_event_session",
        ValueType: SettingValueType.Integer,
        DefaultValue: "50",
        Category: Category,
        Description: "Maximum runtime custom-property definitions attached to a single EventSession. Platform max: 500.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxOptionsPerDefinition = new(
        Key: "custom_properties.max_options_per_definition",
        ValueType: SettingValueType.Integer,
        DefaultValue: "200",
        Category: Category,
        Description: "Maximum option rows allowed per custom-property definition. Platform max: 2000.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxMultiValueRowsPerValue = new(
        Key: "custom_properties.max_multi_value_rows_per_value",
        ValueType: SettingValueType.Integer,
        DefaultValue: "20",
        Category: Category,
        Description: "Maximum per-ordinal value rows allowed for a multi-valued custom property. Platform max: 200.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxDefinitionsPerTemplate = new(
        Key: "custom_properties.max_definitions_per_template",
        ValueType: SettingValueType.Integer,
        DefaultValue: "100",
        Category: Category,
        Description: "Maximum definitions allowed on a single event or session template. Platform max: 1000.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ProjectionRebuildBatchSize = new(
        Key: "custom_properties.projection_rebuild_batch_size",
        ValueType: SettingValueType.Integer,
        DefaultValue: "500",
        Category: Category,
        Description: "Batch size used by the projection rebuild worker when iterating runtime rows. Platform max: 5000.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SyncApplyMaxChangeCount = new(
        Key: "custom_properties.sync_apply_max_change_count",
        ValueType: SettingValueType.Integer,
        DefaultValue: "200",
        Category: Category,
        Description: "Maximum discrete changes allowed in a single template sync apply payload. Platform max: 2000.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SyncApplyMaxPayloadBytes = new(
        Key: "custom_properties.sync_apply_max_payload_bytes",
        ValueType: SettingValueType.Integer,
        DefaultValue: "262144",
        Category: Category,
        Description: "Maximum serialized size in bytes for a single template sync apply payload. Platform max: 4194304.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxDirtyScopePendingPerTenant = new(
        Key: "custom_properties.max_dirty_scope_pending_per_tenant",
        ValueType: SettingValueType.Integer,
        DefaultValue: "10000",
        Category: Category,
        Description: "Maximum pending projection dirty-scope rows per tenant before inline writes are rejected. Platform max: 100000.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ProjectionDiscoveryEnabled = new(
        Key: "custom_properties.projection_discovery_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: Category,
        Description: "Tenant feature flag enabling custom-property projection-backed discovery, search, filter, and export paths.",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        MaxDefinitionsPerTenantPerEntityScope,
        MaxDefinitionsPerEvent,
        MaxDefinitionsPerEventSession,
        MaxOptionsPerDefinition,
        MaxMultiValueRowsPerValue,
        MaxDefinitionsPerTemplate,
        ProjectionRebuildBatchSize,
        SyncApplyMaxChangeCount,
        SyncApplyMaxPayloadBytes,
        MaxDirtyScopePendingPerTenant,
        ProjectionDiscoveryEnabled,
    ];
}
