// ABOUTME: Verifies machine-consumed visitor and configurable-provider setting declarations.
// ABOUTME: Guards conservative defaults, enum names, and instance-to-tenant ownership boundaries.

using System.Text.Json;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;

namespace Event.Domain.UnitTests.Settings;

public sealed class VisitorAccessSettingDefinitionTests
{
    [Test]
    [Arguments(GovernanceSettingKeys.Authentication.PrimaryProviderId, SettingValueType.Integer, "4")]
    [Arguments(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled, SettingValueType.Boolean, "false")]
    [Arguments(GovernanceSettingKeys.Authentication.AtprotoPublicUrl, SettingValueType.String, "\"\"")]
    [Arguments(GovernanceSettingKeys.Authentication.GoogleSsoEnabled, SettingValueType.Boolean, "false")]
    [Arguments(GovernanceSettingKeys.Authentication.GoogleClientId, SettingValueType.String, "\"\"")]
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
    [Arguments(GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy)]
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
    [Arguments(GovernanceSettingKeys.Authentication.GooglePublicSignupUrl)]
    public async Task OnboardingDestinationsHaveNoInventedDefaultAndRemainInstanceOwned(string key)
    {
        var definition = SettingRegistry.Get(key)!;

        await Assert.That(definition.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(JsonSerializer.Deserialize<string>(definition.DefaultValue)).IsEqualTo(string.Empty);
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.IsSensitive).IsFalse();
    }
}
