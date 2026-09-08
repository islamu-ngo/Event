namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class McpSettingDefinitions
{
    public static readonly SettingDefinition Enabled = new(
        Key: GovernanceSettingKeys.Mcp.Enabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Mcp",
        Description: "Enable the API-hosted MCP adapter when the startup MCP ceiling is also enabled",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition EnableLegacySse = new(
        Key: GovernanceSettingKeys.Mcp.EnableLegacySse,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Mcp",
        Description: "Request legacy MCP SSE transport when startup and protocol safety gates permit it",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All => [Enabled, EnableLegacySse];
}
