
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Secrets;

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

    [Test]
    [Arguments("database.local_identity_writer.password", "/database", "DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD")]
    [Arguments("identity_database.local_identity_writer.password", "/database/identity", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD")]
    public async Task LocalWriterPasswordHasItsOwnOptionalInstanceAuthority(
        string key, string path, string environmentName)
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(key);

        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo(environmentName);
        await Assert.That(definition.DefaultInfisicalKey).IsEqualTo(environmentName);
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo(path);
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.AllowedSources).Contains(SecretSourceType.Infisical);
        await Assert.That(definition.AllowedSources).Contains(SecretSourceType.EnvironmentVariable);
        await Assert.That(definition.Requirement).IsEqualTo(SecretRequirement.OptionalCapability);
        await Assert.That(() => SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant,
            Guid.CreateVersion7(), environmentName)).Throws<ArgumentException>();
    }
}
