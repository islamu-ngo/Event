namespace Explore.Domain.Settings.Definitions;

public static class OrganizationSettingDefinitions
{
    public static readonly SettingDefinition VerificationRequired = new(
        Key: "organizations.verification_required",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Organizations",
        Description: "Whether organization verification is required before organizations can operate",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition TenantCanOmitVerification = new(
        Key: "organizations.tenant_can_omit_verification",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Organizations",
        Description: "Whether tenant administrators may omit organization verification requirements",
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition SelfRegistrationEnabled = new(
        Key: "organizations.self_registration_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Organizations",
        Description: "Whether users can self-register organizations",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
        [VerificationRequired, TenantCanOmitVerification, SelfRegistrationEnabled];
}
