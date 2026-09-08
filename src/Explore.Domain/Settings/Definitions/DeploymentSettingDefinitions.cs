namespace Explore.Domain.Settings.Definitions;

public static class DeploymentSettingDefinitions
{
    public static readonly SettingDefinition Mode = new(
        Key: "deployment.mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"MultiTenant\"",
        Category: "System",
        Description: "Deployment mode of the application",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: true,
        AllowedValues: ["SingleTenant", "MultiTenant"]);

    public static IReadOnlyList<SettingDefinition> All => [Mode];
}
