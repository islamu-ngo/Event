namespace Explore.Domain.Settings.Definitions;

public static class DomainSettingDefinitions
{
    public static readonly SettingDefinition InstanceBaseDomain = new(
        Key: "domains.instance_base_domain",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Domains",
        Description: "Instance base domain used for tenant subdomain generation");

    public static readonly SettingDefinition AllowTenantCustomDomain = new(
        Key: "domains.allow_tenant_custom_domain",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Domains",
        Description: "Whether tenant administrators can configure custom domains");

    public static readonly SettingDefinition AdminHost = new(
        Key: "domains.admin_host",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Domains",
        Description: "Dedicated admin/control-plane host for multi-tenant operator access");

    public static readonly SettingDefinition TenantSubdomain = new(
        Key: "domains.tenant_subdomain",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Domains",
        Description: "Tenant subdomain override placeholder",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition TenantCustomDomain = new(
        Key: "domains.tenant_custom_domain",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Domains",
        Description: "Tenant custom domain override placeholder",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
        [InstanceBaseDomain, AdminHost, AllowTenantCustomDomain, TenantSubdomain, TenantCustomDomain];
}
