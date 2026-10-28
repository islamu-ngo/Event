using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationMeasurementSigningKeyTests
{
    [Test]
    public async Task MeasurementSigner_IsInstanceOnlyAndNeverStoredAsTenantSettings()
    {
        var definition = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.ModerationMeasurementSigningPrivateKeyPem);
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.AllowedSources)
            .IsEquivalentTo([SecretSourceType.Infisical, SecretSourceType.EnvironmentVariable]);
        await Assert.That(definition.IsBootstrapSecret).IsTrue();
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/moderation");
        await Assert.That(definition.DefaultInfisicalKey).IsEqualTo("MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM");
        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo("MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TenantCannotBindTheDeploymentMeasurementSigner(bool vault)
    {
        await Assert.That(() => vault
            ? SecretBinding.CreateInfisical(SecretDefinitionRegistry.Keys.ModerationMeasurementSigningPrivateKeyPem,
                SecretScope.Tenant, Guid.CreateVersion7(), "testing", "/moderation",
                "MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM")
            : SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.ModerationMeasurementSigningPrivateKeyPem,
                SecretScope.Tenant, Guid.CreateVersion7(), "MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task MeasurementDatabaseCredentialCannotBecomeTenantConfiguration()
    {
        var definition = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.ModerationMeasurementTestDatabasePassword);
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.AllowedSources)
            .IsEquivalentTo([SecretSourceType.Infisical, SecretSourceType.EnvironmentVariable]);
        await Assert.That(definition.IsBootstrapSecret).IsTrue();
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/moderation");
        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo("MODERATION__MEASUREMENTTESTDATABASE__PASSWORD");
        await Assert.That(() => SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.ModerationMeasurementTestDatabasePassword,
            SecretScope.Tenant, Guid.CreateVersion7(), "MODERATION__MEASUREMENTTESTDATABASE__PASSWORD"))
            .Throws<ArgumentException>();
    }
}
