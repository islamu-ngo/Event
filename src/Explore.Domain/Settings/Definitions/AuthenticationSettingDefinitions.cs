// ABOUTME: Instance-owned provider configuration and explicit public account onboarding declarations.
// ABOUTME: Conservative stored defaults preserve provider-service deployment ownership and onboarding authority.

using Explore.Domain.Constants;

namespace Explore.Domain.Settings.Definitions;

public static class AuthenticationSettingDefinitions
{
    // Defaults describe absent application-managed values, not deployment provider selection.
    // The native provider service still resolves deployment ownership before stored settings.
    public static readonly SettingDefinition PrimaryProviderId = new(
        Key: GovernanceSettingKeys.Authentication.PrimaryProviderId,
        ValueType: SettingValueType.Integer,
        DefaultValue: "4",
        Category: "Authentication",
        Description: "Normalized lookup identifier for the active primary authentication provider",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition KeycloakAuthority = new(
        Key: GovernanceSettingKeys.Authentication.KeycloakAuthority,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Authentication",
        Description: "Keycloak realm authority URL",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition KeycloakClientId = new(
        Key: GovernanceSettingKeys.Authentication.KeycloakClientId,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Authentication",
        Description: "Keycloak OIDC client ID",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition AtprotoLoginEnabled = new(
        Key: GovernanceSettingKeys.Authentication.AtprotoLoginEnabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Authentication",
        Description: "Whether ATProto DID-based authentication is enabled",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition AtprotoPublicUrl = new(
        Key: GovernanceSettingKeys.Authentication.AtprotoPublicUrl,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Authentication",
        Description: "Publicly accessible URL for ATProto OAuth client metadata",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition GoogleSsoEnabled = new(
        Key: GovernanceSettingKeys.Authentication.GoogleSsoEnabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Authentication",
        Description: "Whether Google SSO authentication is enabled",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition GoogleClientId = new(
        Key: GovernanceSettingKeys.Authentication.GoogleClientId,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Authentication",
        Description: "Google OAuth client ID",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition KeycloakPublicOnboardingPolicy = OnboardingPolicy(
        GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy);

    public static readonly SettingDefinition KeycloakPublicSignupUrl = SignupUrl(
        GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl);

    public static readonly SettingDefinition GooglePublicOnboardingPolicy = OnboardingPolicy(
        GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy);

    public static readonly SettingDefinition GooglePublicSignupUrl = SignupUrl(
        GovernanceSettingKeys.Authentication.GooglePublicSignupUrl);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        PrimaryProviderId,
        KeycloakAuthority,
        KeycloakClientId,
        AtprotoLoginEnabled,
        AtprotoPublicUrl,
        GoogleSsoEnabled,
        GoogleClientId,
        KeycloakPublicOnboardingPolicy,
        KeycloakPublicSignupUrl,
        GooglePublicOnboardingPolicy,
        GooglePublicSignupUrl
    ];

    private static SettingDefinition OnboardingPolicy(string key) => new(
        Key: key,
        ValueType: SettingValueType.String,
        DefaultValue: "\"Unknown\"",
        Category: "Authentication",
        Description: "Operator-declared public account onboarding policy; independent of existing-account login",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        AllowedValues: ["Unknown", "Allowed", "Denied"]);

    private static SettingDefinition SignupUrl(string key) => new(
        Key: key,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Authentication",
        Description: "Operator-configured absolute HTTPS public onboarding destination; requires Allowed policy",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);
}
