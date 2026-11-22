
using System.Text.Json;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.Settings;

namespace Event.Domain.UnitTests.Settings;

public sealed class VisitorAccessSettingDefinitionTests
{
    [Test]
    [Arguments(GovernanceSettingKeys.Authentication.PrimaryProviderId, SettingValueType.Integer, "4")]
    [Arguments(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled, SettingValueType.Boolean, "false")]
    [Arguments(GovernanceSettingKeys.Authentication.AtprotoPublicUrl, SettingValueType.String, "\"\"")]
    [Arguments(GovernanceSettingKeys.Authentication.KeycloakAuthority, SettingValueType.String, "\"\"")]
    [Arguments(GovernanceSettingKeys.Authentication.KeycloakClientId, SettingValueType.String, "\"\"")]
    public async Task NativeProviderMetadataSupportsCoordinatedWritesWithoutInventedConfiguration(
        string key, SettingValueType valueType, string defaultValue)
    {
        var definition = SettingRegistry.Get(key);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.ValueType).IsEqualTo(valueType);
        await Assert.That(definition.DefaultValue).IsEqualTo(defaultValue);
        await Assert.That(definition.Category).IsEqualTo("Authentication");
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.IsLockable).IsTrue();
        await Assert.That(definition.IsSensitive).IsFalse();
    }

    [Test]
    public async Task VisitorModeIsLockableInstanceToTenantPolicyWithCapabilityBoundedDefault()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.PublicExperience.VisitorAccessMode)!;

        await Assert.That(definition.Key).IsEqualTo("public_experience.visitor_access_mode");
        await Assert.That(definition.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(JsonSerializer.Deserialize<string>(definition.DefaultValue))
            .IsEqualTo(nameof(VisitorAccessMode.FullRegistrationAndAuth));
        await Assert.That(definition.AllowedValues).IsEquivalentTo(Enum.GetNames<VisitorAccessMode>());
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
        await Assert.That(definition.IsLockable).IsTrue();
    }

    [Test]
    [Arguments(GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy)]
    public async Task ConfigurableProviderPolicyDefaultsUnknownAndCannotBeTenantOverridden(string key)
    {
        var definition = SettingRegistry.Get(key)!;

        await Assert.That(definition.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(JsonSerializer.Deserialize<string>(definition.DefaultValue))
            .IsEqualTo(nameof(PublicOnboardingPolicy.Unknown));
        await Assert.That(definition.AllowedValues).IsEquivalentTo(Enum.GetNames<PublicOnboardingPolicy>());
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Instance);
    }

    [Test]
    [Arguments(GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl)]
    public async Task OnboardingDestinationsHaveNoInventedDefaultAndRemainInstanceOwned(string key)
    {
        var definition = SettingRegistry.Get(key)!;

        await Assert.That(definition.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(JsonSerializer.Deserialize<string>(definition.DefaultValue)).IsEqualTo(string.Empty);
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.IsSensitive).IsFalse();
    }

    [Test]
    [Arguments(GovernanceSettingKeys.Authentication.GoogleSsoEnabled)]
    [Arguments(GovernanceSettingKeys.Authentication.GoogleClientId)]
    [Arguments(GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy)]
    [Arguments(GovernanceSettingKeys.Authentication.GooglePublicSignupUrl)]
    public async Task ArchivedGoogleSettingsCannotBecomeEditableProviderDefinitions(string key)
    {
        await Assert.That(SettingRegistry.Get(key)).IsNull();
    }

    [Test]
    public async Task TenantUserEnrollmentDefaultsOpenAndRespectsInstanceLocks()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.Tenants.UserRegistrationEnabled)!;

        await Assert.That(definition.Key).IsEqualTo("tenants.user_registration_enabled");
        await Assert.That(definition.DefaultValue).IsEqualTo("true");
        await Assert.That(definition.ValueType).IsEqualTo(SettingValueType.Boolean);
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
        await Assert.That(definition.IsLockable).IsTrue();
        await Assert.That(definition.IsSensitive).IsFalse();
    }

    [Test]
    [Arguments(null, false, null, true)]
    [Arguments("false", false, null, false)]
    [Arguments("true", false, "false", false)]
    [Arguments("false", false, "true", true)]
    [Arguments("false", true, "true", false)]
    [Arguments("true", true, "false", true)]
    public async Task EnrollmentInheritanceUsesTheSelectedCompleteScalar(
        string? instance, bool locked, string? tenant, bool expected)
    {
        await Assert.That(TenantUserRegistrationPolicy.ResolveEnabled(instance, locked, tenant)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("null")]
    [Arguments("\"true\"")]
    [Arguments("1")]
    [Arguments("not-json")]
    public async Task MalformedEnrollmentCannotFallBackToAnOpenDefault(string raw)
    {
        await Assert.That(() => TenantUserRegistrationPolicy.ResolveEnabled(raw, false, null))
            .Throws<InvalidOperationException>();
    }
}
