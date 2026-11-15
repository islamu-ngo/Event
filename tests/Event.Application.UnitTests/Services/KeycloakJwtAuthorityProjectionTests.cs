using System.Text.Json;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace Event.Application.UnitTests.Services;

public sealed class KeycloakJwtAuthorityProjectionTests
{
    [Test]
    public async Task StoredPublicMetadataSelectsKeycloakWithoutConfidentialCredentials()
    {
        await Assert.That(AuthProviderConfigurationService.ProjectKeycloakJwtAuthority(
            Stored(), Configuration())).IsEqualTo("https://stored.example.test/realms/instance");
    }

    [Test]
    [Arguments("Local")]
    [Arguments("Atproto")]
    public async Task DeploymentPrimaryProviderOverridesStoredKeycloak(string provider)
    {
        await Assert.That(AuthProviderConfigurationService.ProjectKeycloakJwtAuthority(
            Stored(), Configuration(("Authentication:Provider", provider)))).IsNull();
    }

    [Test]
    public async Task DeploymentManagedPublicCoordinatesOverrideStoredCoordinates()
    {
        var configuration = Configuration(
            ("Keycloak:Authority", "https://managed.example.test/realms/instance"),
            ("Keycloak:ClientId", "managed-browser"),
            ("Secrets:Ownership:DeploymentManagedKeys:0", "Keycloak:Authority"),
            ("Secrets:Ownership:DeploymentManagedKeys:1", "Keycloak:ClientId"));

        await Assert.That(AuthProviderConfigurationService.ProjectKeycloakJwtAuthority(
            Stored(), configuration)).IsEqualTo("https://managed.example.test/realms/instance");
    }

    [Test]
    public async Task IncompletePublicMetadataCannotSelectAJwtAuthority()
    {
        var stored = Stored();
        stored.Remove(GovernanceSettingKeys.Authentication.KeycloakClientId);
        await Assert.That(AuthProviderConfigurationService.ProjectKeycloakJwtAuthority(
            stored, Configuration())).IsNull();
    }

    private static Dictionary<string, SystemSetting> Stored() => new()
    {
        [GovernanceSettingKeys.Authentication.PrimaryProviderId] = new()
        {
            SettingKey = GovernanceSettingKeys.Authentication.PrimaryProviderId,
            Value = JsonSerializer.Serialize((int)AuthenticationProviderKind.Keycloak)
        },
        [GovernanceSettingKeys.Authentication.KeycloakAuthority] = new()
        {
            SettingKey = GovernanceSettingKeys.Authentication.KeycloakAuthority,
            Value = JsonSerializer.Serialize("https://stored.example.test/realms/instance")
        },
        [GovernanceSettingKeys.Authentication.KeycloakClientId] = new()
        {
            SettingKey = GovernanceSettingKeys.Authentication.KeycloakClientId,
            Value = JsonSerializer.Serialize("stored-browser")
        }
    };

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();
}
