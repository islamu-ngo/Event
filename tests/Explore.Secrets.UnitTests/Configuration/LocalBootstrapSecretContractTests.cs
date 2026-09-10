
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class LocalBootstrapSecretContractTests
{
    private const string SettingKey = "authentication.local.bootstrap_password";

    [Test]
    public async Task BootstrapPasswordUsesDedicatedDeploymentCoordinates()
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(SettingKey);

        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD");
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/api");
        await Assert.That(definition.DefaultInfisicalKey).IsEqualTo("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD");
        await Assert.That(definition.IsBootstrapSecret).IsTrue();
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.AllowedSources).IsEquivalentTo(
            [SecretSourceType.Infisical, SecretSourceType.EnvironmentVariable]);
    }

    [Test]
    public async Task TenantCannotBindAnInitialAdministratorPassword()
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(SettingKey);

        await Assert.That(() => SecretBinding.CreateEnvironmentVariable(
            settingKey: SettingKey,
            scope: SecretScope.Tenant,
            scopeId: Guid.CreateVersion7(),
            variableName: definition.DefaultEnvironmentVariableName!)).Throws<ArgumentException>();
    }

    [Test]
    public async Task BootstrapPasswordCannotUseRuntimeCredentialRotation()
    {
        SecretRotationProfile profile = SecretDefinitionRegistry.GetRotationProfile(SettingKey);

        await Assert.That(profile.Mode).IsEqualTo(SecretRotationMode.UnsupportedLive);
    }
}
