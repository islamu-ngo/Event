using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class AdmissionScannerCapabilitySecretDefinitionTests
{
    [Test]
    public async Task ScannerCapabilityUsesDedicatedInstanceSecretCoordinates()
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(
            SecretDefinitionRegistry.Keys.Admissions.ScannerCapabilityHmacKey);

        await Assert.That(definition.Key).IsEqualTo("admissions.scanner_capability_hmac_key");
        await Assert.That(definition.Key)
            .IsNotEqualTo(SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey);
        await Assert.That(definition.Key)
            .IsNotEqualTo(SecretDefinitionRegistry.Keys.Admissions.RecoveryCapabilityHmacKey);
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/admissions");
        await Assert.That(definition.DefaultInfisicalKey)
            .IsEqualTo("ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY");
        await Assert.That(definition.DefaultEnvironmentVariableName)
            .IsEqualTo("ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY");
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.AllowedSources).Contains(SecretSourceType.Infisical);
        await Assert.That(definition.AllowedSources).Contains(SecretSourceType.EnvironmentVariable);
        await Assert.That(definition.AllowedSources).Contains(SecretSourceType.EnvironmentVariable);
        await Assert.That(definition.IsBootstrapSecret).IsFalse();
    }
}
